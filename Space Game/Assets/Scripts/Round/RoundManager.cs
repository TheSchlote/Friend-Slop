using System.Collections;
using System.Collections.Generic;
using FriendSlop.Hazards;
using FriendSlop.Loot;
using FriendSlop.Player;
using FriendSlop.SceneManagement;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public class RoundManager : NetworkBehaviour
    {
        public static RoundManager Instance { get; private set; }
        public static event System.Action LocalTeleporterFlashRequested;

        [SerializeField] private int quota = 500;
        [SerializeField] private float roundLengthSeconds = 0f;
        [SerializeField] private Transform[] playerSpawnPoints;

        [Header("Ship Lobby")]
        [SerializeField] private Transform[] shipSpawnPoints;
        [SerializeField] private bool returnToShipOnRoundEnd = true;

        [Header("Planet Progression")]
        [SerializeField] private PlanetCatalog planetCatalog;
        [SerializeField] private PlanetDefinition startingPlanet;

        [Header("Objective")]
        [SerializeField] private RoundObjective defaultObjective;

        public NetworkVariable<RoundPhase> Phase = new(RoundPhase.Lobby);
        public NetworkVariable<float> TimeRemaining = new(0f);
        public NetworkVariable<int> CollectedValue = new(0);
        public NetworkVariable<int> Quota = new(500);
        public NetworkVariable<bool> HasCockpit = new(false);
        public NetworkVariable<bool> HasWings = new(false);
        public NetworkVariable<bool> HasEngine = new(false);
        public NetworkVariable<bool> RocketAssembled = new(false);
        public NetworkVariable<int> PlayersBoarded = new(0);
        public NetworkVariable<int> PlayersReady = new(0);
        public NetworkVariable<int> PlayersExpectedToLoad = new(0);
        public NetworkVariable<int> CurrentTier = new(1);
        public NetworkVariable<int> CurrentPlanetCatalogIndex = new(-1);
        public NetworkVariable<int> SelectedNextPlanetCatalogIndex = new(-1);

        // Catalog indexes the host is currently offered as next-planet choices. The server
        // re-rolls this on each Success. When the next tier has more than MaxNextPlanetChoices
        // planets we randomly pick that many; otherwise the full tier is offered.
        public NetworkList<int> NextPlanetChoiceIndices;
        public const int MaxNextPlanetChoices = 2;

        private readonly List<NetworkLootItem> lootItems = new();
        private readonly HashSet<ulong> boardedPlayerIds = new();
        private readonly HashSet<ulong> _readyPlayerIds = new();
        private float _loadingTimeout;
        private const float LoadingTimeoutSeconds = 15f;
        private Coroutine _transitionCoroutine;
        private const float TransitionFadeSeconds = 1.0f;
        private const float TransitionHoldSeconds = 0.8f;

        // Server-side bookkeeping: which planet scene we last asked Netcode to load.
        // Used to unload the previous planet scene when switching to a new one.
        private string _activePlanetScenePath = string.Empty;
        // Per-instance dedup so the "scene not in Build Settings" warning fires once per
        // missing path instead of every time ApplyActivePlanetEnvironment is invoked.
        private readonly HashSet<string> _warnedMissingPlanetScenes = new();

        public void ConfigureSpawnPoints(Transform[] spawnPoints)
        {
            playerSpawnPoints = spawnPoints;
        }

        public void ConfigureShipSpawnPoints(Transform[] spawnPoints)
        {
            shipSpawnPoints = spawnPoints;
        }

        private void Awake()
        {
            Instance = this;
            // NetworkList must exist before OnNetworkSpawn so the server's first writes replicate.
            NextPlanetChoiceIndices = new NetworkList<int>();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
            }

            CurrentPlanetCatalogIndex.OnValueChanged += OnCurrentPlanetCatalogIndexChanged;
            PlanetEnvironment.Registered += HandlePlanetEnvironmentRegistered;

            if (IsServer)
            {
                ServerSetPhase(RoundPhase.Lobby);
                TimeRemaining.Value = roundLengthSeconds;
                Quota.Value = quota;

                var initialPlanet = startingPlanet != null
                    ? startingPlanet
                    : (planetCatalog != null ? planetCatalog.GetFirstForTier(1) : null);
                if (initialPlanet != null)
                {
                    CurrentTier.Value = initialPlanet.Tier;
                    CurrentPlanetCatalogIndex.Value = planetCatalog != null ? planetCatalog.IndexOf(initialPlanet) : -1;
                    ApplyPlanetOverrides(initialPlanet);
                    Quota.Value = quota;
                    TimeRemaining.Value = roundLengthSeconds;
                }

                ServerMovePlayersToShip(revivePlayers: true);
            }

            ApplyActivePlanetEnvironment();
        }

        public override void OnNetworkDespawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;
            }

            CurrentPlanetCatalogIndex.OnValueChanged -= OnCurrentPlanetCatalogIndexChanged;
            PlanetEnvironment.Registered -= HandlePlanetEnvironmentRegistered;
        }

        private void OnCurrentPlanetCatalogIndexChanged(int previous, int current)
        {
            ApplyActivePlanetEnvironment();
        }

        private void HandlePlanetEnvironmentRegistered(PlanetEnvironment env)
        {
            // A planet scene that just additively loaded brings its env with it. Two
            // cases trigger a re-bind: the registered env is the active planet's env,
            // or it's a same-tier env that the active planet falls back to (catalog
            // entries without their own dedicated environment, like DeepHaul borrowing
            // Rusty Moon's scene).
            if (env == null || env.Planet == null) return;
            var current = CurrentPlanet;
            if (current == null) return;
            if (env.Planet == current || env.Planet.Tier == current.Tier)
                ApplyActivePlanetEnvironment();
        }

        private void ApplyActivePlanetEnvironment()
        {
            var planet = CurrentPlanet;

            // Server: reconcile per-planet scene loads. If the active planet has a scene
            // assigned, ensure it's loaded; if a previous planet scene is still around and
            // doesn't match, unload it. Scene loads are async - when the env Awakes inside
            // the new scene it fires Registered, which calls back into this method.
            if (IsServer)
                ServerReconcilePlanetScenes(planet);

            // Search AllEnvironments so we can find and enable planets whose roots are disabled.
            PlanetEnvironment activeEnv = null;
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env != null && env.Planet == planet) { activeEnv = env; break; }
            }

            // Fallback: planets defined in the catalog without a dedicated scene environment
            // (e.g. newly authored variants) reuse any environment for the same tier so they
            // are playable while real environments are being built.
            if (activeEnv == null && planet != null)
            {
                for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
                {
                    var env = PlanetEnvironment.AllEnvironments[i];
                    if (env != null && env.Planet != null && env.Planet.Tier == planet.Tier)
                    {
                        activeEnv = env;
                        break;
                    }
                }
            }

            // Toggle planet roots. For planets whose visual content lives in a separate
            // scene object (ContentRoot != gameObject), toggle that too. Scene-loaded envs
            // skip the GameObject toggle (their visibility is owned by scene load/unload),
            // but we still flip their launchpad's active flag so inactive planets don't
            // keep accepting boarding/loot when multiple planet scenes happen to be loaded
            // (typical in-editor when scenes are left open for authoring).
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env == null) continue;
                var want = env == activeEnv;

                if (IsSceneLoadedPlanet(env))
                {
                    env.SetActiveForRound(want);
                    continue;
                }

                if (env.gameObject.activeSelf != want)
                    env.gameObject.SetActive(want);
                var content = env.ContentRoot;
                if (content != env.gameObject && content.activeSelf != want)
                    content.SetActive(want);
            }

            if (activeEnv != null)
                activeEnv.SetActiveForRound(true);

            if (IsServer && activeEnv != null && activeEnv.PlayerSpawnPoints != null && activeEnv.PlayerSpawnPoints.Length > 0)
                ConfigureSpawnPoints(activeEnv.PlayerSpawnPoints);
        }

        private void ServerReconcilePlanetScenes(PlanetDefinition planet)
        {
            // Defer to next frame so this never runs while NetworkManager's
            // HostServerInitialize is still on the stack - mid-init SceneManager.LoadScene
            // calls land in a half-initialized state and have produced "scene event in
            // progress" / NREs in startup logs. The legacy fallback in
            // ApplyActivePlanetEnvironment keeps the active env bound from the prototype
            // scene during the one-frame gap, so gameplay doesn't notice.
            StartCoroutine(ServerReconcilePlanetScenesDeferred(planet));
        }

        private IEnumerator ServerReconcilePlanetScenesDeferred(PlanetDefinition planet)
        {
            yield return null;
            if (this == null || !IsServer) yield break;

            var service = NetworkSceneTransitionService.Instance;
            var sceneOwner = ResolveSceneOwner(planet);
            var hasScene = sceneOwner != null;
            var targetPath = hasScene ? sceneOwner.PlanetScene.ScenePath : string.Empty;

            // Unload the previously-loaded planet scene if we're switching away from it.
            if (!string.IsNullOrEmpty(_activePlanetScenePath) && _activePlanetScenePath != targetPath)
            {
                if (service != null)
                    service.ServerUnloadScenePath(_activePlanetScenePath);
                _activePlanetScenePath = string.Empty;
            }

            if (!hasScene) yield break;

            // Build-settings sanity check: a planet asset can reference a scene that the
            // extractor hasn't authored yet. Skip the additive load with a warning so the
            // host stays alive on a fresh checkout - the nested fallback env will play.
            if (UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(targetPath) < 0)
            {
                if (_warnedMissingPlanetScenes.Add(targetPath))
                {
                    Debug.LogWarning(
                        $"RoundManager: planet scene '{targetPath}' is not in Build Settings; skipping additive load. " +
                        "(Run Tools/Friend Slop/Extract Tier 1 Into Scene to author it.)");
                }
                yield break;
            }

            if (service != null && !service.WasServerSceneLoadStarted(targetPath))
            {
                service.ServerLoadScene(sceneOwner.PlanetScene);
            }
            _activePlanetScenePath = targetPath;
        }

        // True when an env exists that ApplyActivePlanetEnvironment would bind to for
        // this planet - either the planet's own env, or any same-tier env if the planet
        // is using the catalog fallback. Mirrors the search in Apply so the transition
        // wait doesn't time out for catalog entries that share an environment.
        private static bool IsBindableEnvRegistered(PlanetDefinition planet)
        {
            if (planet == null) return false;
            if (PlanetEnvironment.FindFor(planet) != null) return true;
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env != null && env.Planet != null && env.Planet.Tier == planet.Tier)
                    return true;
            }
            return false;
        }

        // Resolves which planet definition's scene actually backs this planet at runtime.
        // Returns the planet itself if it has a dedicated scene, otherwise looks for any
        // other catalog entry at the same tier that does. Tier-2 catalog entries like
        // DeepHaul/GhostShift currently share Rusty Moon's environment via this fallback.
        private PlanetDefinition ResolveSceneOwner(PlanetDefinition planet)
        {
            if (planet == null) return null;
            if (planet.HasPlanetScene) return planet;
            if (planetCatalog == null) return null;

            for (var i = 0; i < planetCatalog.Count; i++)
            {
                var candidate = planetCatalog.GetByIndex(i);
                if (candidate == null) continue;
                if (candidate == planet) continue;
                if (candidate.Tier != planet.Tier) continue;
                if (candidate.HasPlanetScene) return candidate;
            }
            return null;
        }

        private static bool IsSceneLoadedPlanet(PlanetEnvironment env)
        {
            if (env == null || env.Planet == null) return false;
            if (!env.Planet.HasPlanetScene) return false;
            // Treat envs as scene-loaded only when their actual scene path matches the
            // PlanetDefinition's assigned scene path. Otherwise they're a nested fallback
            // in the bootstrap scene and should still be toggled like before.
            var envScenePath = GameScenePathUtility.NormalizePath(env.gameObject.scene.path);
            return string.Equals(envScenePath, env.Planet.PlanetScene.ScenePath,
                System.StringComparison.OrdinalIgnoreCase);
        }

        private void Update()
        {
            if (!IsServer) return;

            if (Phase.Value == RoundPhase.Transitioning) return;

            if (Phase.Value == RoundPhase.Loading)
            {
                _loadingTimeout -= Time.deltaTime;
                var allReady = PlayersExpectedToLoad.Value > 0
                    && _readyPlayerIds.Count >= PlayersExpectedToLoad.Value;
                if (allReady || _loadingTimeout <= 0f)
                    ServerSetPhase(RoundPhase.Active);
                return;
            }

            if (Phase.Value != RoundPhase.Active) return;

            if (roundLengthSeconds > 0f)
            {
                TimeRemaining.Value = Mathf.Max(0f, TimeRemaining.Value - Time.deltaTime);
            }

            var objective = ActiveObjective;
            if (objective != null)
            {
                var status = objective.Evaluate(this);
                if (status == ObjectiveStatus.Success)
                {
                    ServerSetPhase(RoundPhase.Success);
                    return;
                }
                if (status == ObjectiveStatus.Failed)
                {
                    ServerSetPhase(RoundPhase.Failed);
                    return;
                }
            }
            else if (roundLengthSeconds > 0f && TimeRemaining.Value <= 0f && CollectedValue.Value < Quota.Value)
            {
                // Legacy fallback when no objective is configured anywhere.
                ServerSetPhase(RoundPhase.Failed);
                return;
            }

            CheckAllDeadCondition();
        }

        private void CheckAllDeadCondition()
        {
            var players = NetworkFirstPersonController.ActivePlayers;
            if (players.Count == 0) return;
            foreach (var player in players)
            {
                if (player != null && player.IsSpawned && !player.IsDead)
                    return;
            }
            ServerSetPhase(RoundPhase.AllDead);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartRoundServerRpc(RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;

            ServerStartRound();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRestartRoundServerRpc(RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;

            ServerStartRound();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSelectNextPlanetServerRpc(int catalogIndex, RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;

            var planet = GetCatalogPlanet(catalogIndex);
            if (planet == null) return;
            if (planet.Tier != NextTier) return;
            // Only allow picking from the offered choices when the server has rolled them.
            if (!IsOfferedNextPlanetIndex(catalogIndex)) return;
            SelectedNextPlanetCatalogIndex.Value = catalogIndex;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTravelToNextPlanetServerRpc(RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;
            if (Phase.Value != RoundPhase.Success) return;

            ServerAdvanceToNextPlanet();
        }

        public PlanetCatalog Catalog => planetCatalog;
        public int NextTier => Mathf.Min(CurrentTier.Value + 1, PlanetCatalog.MaxTier);
        public bool HasReachedFinalTier => CurrentTier.Value >= PlanetCatalog.MaxTier;
        public PlanetDefinition CurrentPlanet => GetCatalogPlanet(CurrentPlanetCatalogIndex.Value);
        public PlanetDefinition SelectedNextPlanet => GetCatalogPlanet(SelectedNextPlanetCatalogIndex.Value);
        public RoundObjective ActiveObjective
        {
            get
            {
                var planet = CurrentPlanet;
                return planet != null && planet.Objective != null ? planet.Objective : defaultObjective;
            }
        }
        public bool HasActiveTimer => roundLengthSeconds > 0f;

        public void ServerSetTimer(float seconds)
        {
            if (!IsServer) return;
            roundLengthSeconds = Mathf.Max(0f, seconds);
            TimeRemaining.Value = roundLengthSeconds;
        }

        public List<PlanetDefinition> GetNextTierCandidates()
        {
            return planetCatalog != null
                ? planetCatalog.GetPlanetsForTier(NextTier)
                : new List<PlanetDefinition>();
        }

        // Returns the random subset offered to the host on the current Success screen. When
        // the server hasn't rolled choices yet (e.g. before any round has ended) this returns
        // the full tier list so menus stay populated.
        public List<PlanetDefinition> GetOfferedNextPlanetChoices()
        {
            var result = new List<PlanetDefinition>();
            if (NextPlanetChoiceIndices != null && NextPlanetChoiceIndices.Count > 0)
            {
                for (var i = 0; i < NextPlanetChoiceIndices.Count; i++)
                {
                    var planet = GetCatalogPlanet(NextPlanetChoiceIndices[i]);
                    if (planet != null && planet.Tier == NextTier) result.Add(planet);
                }
            }
            if (result.Count == 0) return GetNextTierCandidates();
            return result;
        }

        private bool IsOfferedNextPlanetIndex(int catalogIndex)
        {
            // No rolled offer yet (pre-Success or final tier): allow any same-tier pick so
            // host-side flows that select up-front still work.
            if (NextPlanetChoiceIndices == null || NextPlanetChoiceIndices.Count == 0) return true;
            for (var i = 0; i < NextPlanetChoiceIndices.Count; i++)
                if (NextPlanetChoiceIndices[i] == catalogIndex) return true;
            return false;
        }

        private void ServerRollNextPlanetChoices()
        {
            if (!IsServer || NextPlanetChoiceIndices == null) return;
            NextPlanetChoiceIndices.Clear();
            if (planetCatalog == null || HasReachedFinalTier) return;

            var pool = GetNextTierCandidates();
            if (pool.Count == 0) return;

            // <= MaxNextPlanetChoices candidates: show the whole tier so players still have
            // a path forward. Above that, randomly sample MaxNextPlanetChoices distinct planets.
            if (pool.Count <= MaxNextPlanetChoices)
            {
                for (var i = 0; i < pool.Count; i++)
                {
                    var idx = planetCatalog.IndexOf(pool[i]);
                    if (idx >= 0) NextPlanetChoiceIndices.Add(idx);
                }
                return;
            }

            for (var i = 0; i < MaxNextPlanetChoices && pool.Count > 0; i++)
            {
                var pickIndex = Random.Range(0, pool.Count);
                var planet = pool[pickIndex];
                pool.RemoveAt(pickIndex);
                var idx = planetCatalog.IndexOf(planet);
                if (idx >= 0) NextPlanetChoiceIndices.Add(idx);
            }
        }

        private PlanetDefinition GetCatalogPlanet(int index)
        {
            return planetCatalog != null ? planetCatalog.GetByIndex(index) : null;
        }

        private void ServerAdvanceToNextPlanet()
        {
            if (!IsServer) return;
            if (HasReachedFinalTier)
            {
                SelectedNextPlanetCatalogIndex.Value = -1;
                ServerStartRound();
                return;
            }

            var next = SelectedNextPlanet;
            if (next == null || next.Tier != NextTier)
            {
                // Default to the first option the host was actually offered, not the first
                // catalog match - the offered list is what the menus showed them.
                var offered = GetOfferedNextPlanetChoices();
                next = offered.Count > 0 ? offered[0] : null;
            }

            if (next == null)
            {
                SelectedNextPlanetCatalogIndex.Value = -1;
                ServerStartRound();
                return;
            }

            var nextIndex = planetCatalog != null ? planetCatalog.IndexOf(next) : -1;
            // Keep SelectedNextPlanetCatalogIndex set during transition so clients can display the destination.
            SelectedNextPlanetCatalogIndex.Value = nextIndex;

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(ServerTransitionToPlanet(next, nextIndex));
        }

        private IEnumerator ServerTransitionToPlanet(PlanetDefinition next, int nextIndex)
        {
            ServerSetPhase(RoundPhase.Transitioning);

            // Wait for clients to fade to black before swapping the planet content.
            yield return new WaitForSeconds(TransitionFadeSeconds);

            CurrentTier.Value = next.Tier;
            CurrentPlanetCatalogIndex.Value = nextIndex; // triggers OnCurrentPlanetCatalogIndexChanged on all clients
            ApplyPlanetOverrides(next);

            // Brief hold so the loading screen is readable before the round starts.
            yield return new WaitForSeconds(TransitionHoldSeconds);

            // Don't start the next round until an env we can bind to is registered.
            // Otherwise RespawnPlayersAtPlanet would teleport players to the previous
            // planet's spawn transforms, which the deferred unload just destroyed.
            // Same fallback as ApplyActivePlanetEnvironment: accept an exact match OR
            // any same-tier env, so catalog entries without their own scene (DeepHaul,
            // GhostShift, etc.) succeed once the fallback scene's env registers.
            const float maxEnvWaitSeconds = 10f;
            var envWaitStart = Time.time;
            while (!IsBindableEnvRegistered(next)
                   && Time.time - envWaitStart < maxEnvWaitSeconds)
            {
                yield return null;
            }

            SelectedNextPlanetCatalogIndex.Value = -1;
            _transitionCoroutine = null;
            ServerStartRound();
        }

        private void ApplyPlanetOverrides(PlanetDefinition planet)
        {
            if (planet == null) return;
            if (planet.QuotaOverride > 0) quota = planet.QuotaOverride;
            if (planet.RoundLengthOverride > 0f) roundLengthSeconds = planet.RoundLengthOverride;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReportLoadedServerRpc(RpcParams rpcParams = default)
        {
            if (Phase.Value != RoundPhase.Loading) return;
            if (_readyPlayerIds.Add(rpcParams.Receive.SenderClientId))
            {
                PlayersReady.Value = _readyPlayerIds.Count;
                if (_readyPlayerIds.Count >= PlayersExpectedToLoad.Value)
                    ServerSetPhase(RoundPhase.Active);
            }
        }

        public void ServerStartRound()
        {
            if (!IsServer)
                return;

            RefreshLootCache();
            CollectedValue.Value = 0;
            TimeRemaining.Value = roundLengthSeconds;
            Quota.Value = quota;
            HasCockpit.Value = false;
            HasWings.Value = false;
            HasEngine.Value = false;
            RocketAssembled.Value = false;
            boardedPlayerIds.Clear();
            PlayersBoarded.Value = 0;

            foreach (var loot in lootItems)
            {
                if (loot != null)
                    loot.ServerReset();
            }

            var monsters = FindObjectsByType<RoamingMonster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var monster in monsters)
                monster.ServerReset();

            _readyPlayerIds.Clear();
            PlayersReady.Value = 0;
            PlayersExpectedToLoad.Value = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 1;
            _loadingTimeout = LoadingTimeoutSeconds;

            // The server (host) is always immediately ready - it initiated the round and doesn't need
            // to sync. Remote clients report via ReportLoadedServerRpc after they receive the phase change.
            if (NetworkManager != null)
            {
                _readyPlayerIds.Add(NetworkManager.ServerClientId);
                PlayersReady.Value = _readyPlayerIds.Count;
            }

            // Let the active objective seed any per-objective state (timer overrides,
            // bumped quota, etc.) before we transition into Loading.
            ActiveObjective?.ServerInitialize(this);

            RespawnPlayersAtPlanet();
            ServerSetPhase(RoundPhase.Loading);
        }

        public void ServerDepositLoot(NetworkLootItem loot)
        {
            if (!IsServer || loot == null || loot.IsShipPart || Phase.Value != RoundPhase.Active || loot.IsDeposited.Value)
                return;

            CollectedValue.Value += loot.Value;
            loot.ServerDeposit();
        }

        public void ServerSubmitToLaunchpad(NetworkLootItem loot)
        {
            if (!IsServer || loot == null || Phase.Value != RoundPhase.Active || loot.IsDeposited.Value)
                return;

            if (!loot.IsShipPart)
            {
                ServerDepositLoot(loot);
                return;
            }

            switch (loot.ShipPartType)
            {
                case ShipPartType.Cockpit:
                    if (HasCockpit.Value) return;
                    HasCockpit.Value = true;
                    break;
                case ShipPartType.Wings:
                    if (HasWings.Value) return;
                    HasWings.Value = true;
                    break;
                case ShipPartType.Engine:
                    if (HasEngine.Value) return;
                    HasEngine.Value = true;
                    break;
            }

            loot.ServerDeposit();

            if (RoundStateUtility.AreAllShipPartsInstalled(HasCockpit.Value, HasWings.Value, HasEngine.Value))
            {
                RocketAssembled.Value = true;
                CheckLaunchCondition();
            }
        }

        public void ServerPlayerBoarded(ulong clientId)
        {
            if (!IsServer || Phase.Value != RoundPhase.Active)
                return;

            if (boardedPlayerIds.Add(clientId))
            {
                UpdateBoardedPlayerCount();
                CheckLaunchCondition();
            }
        }

        public void ServerPlayerUnboarded(ulong clientId)
        {
            if (!IsServer)
                return;

            if (boardedPlayerIds.Remove(clientId))
            {
                UpdateBoardedPlayerCount();
            }
        }

        private void CheckLaunchCondition()
        {
            var objective = ActiveObjective;
            if (objective != null)
            {
                // Re-evaluate immediately on relevant gameplay events; otherwise Update handles polling.
                if (Phase.Value != RoundPhase.Active) return;
                var status = objective.Evaluate(this);
                if (status == ObjectiveStatus.Success) ServerSetPhase(RoundPhase.Success);
                else if (status == ObjectiveStatus.Failed) ServerSetPhase(RoundPhase.Failed);
                return;
            }

            var connectedPlayerCount = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            if (!RoundStateUtility.IsLaunchReady(RocketAssembled.Value, PlayersBoarded.Value, connectedPlayerCount))
                return;

            ServerSetPhase(RoundPhase.Success);
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;

            if (Phase.Value == RoundPhase.Loading)
            {
                var wasReady = _readyPlayerIds.Remove(clientId);
                var loadingCounts = RoundStateUtility.RemoveDisconnectedLoadingPlayer(
                    PlayersExpectedToLoad.Value,
                    PlayersReady.Value,
                    wasReady);
                PlayersExpectedToLoad.Value = loadingCounts.ExpectedToLoad;
                PlayersReady.Value = loadingCounts.ReadyCount;

                if (PlayersExpectedToLoad.Value <= 0 || PlayersReady.Value >= PlayersExpectedToLoad.Value)
                {
                    ServerSetPhase(RoundPhase.Active);
                }
            }

            if (boardedPlayerIds.Remove(clientId))
            {
                UpdateBoardedPlayerCount();
                CheckLaunchCondition();
            }
        }

        private void UpdateBoardedPlayerCount()
        {
            if (NetworkManager == null)
            {
                PlayersBoarded.Value = boardedPlayerIds.Count;
                return;
            }

            var connectedBoardedPlayers = 0;
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                if (boardedPlayerIds.Contains(clientId))
                {
                    connectedBoardedPlayers++;
                }
            }

            PlayersBoarded.Value = connectedBoardedPlayers;
        }

        public static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            var minutes = Mathf.FloorToInt(seconds / 60f);
            var remainingSeconds = Mathf.FloorToInt(seconds % 60f);
            return $"{minutes:00}:{remainingSeconds:00}";
        }

        private void RefreshLootCache()
        {
            lootItems.Clear();
            lootItems.AddRange(FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        private void RespawnPlayersAtPlanet()
        {
            if (playerSpawnPoints == null || playerSpawnPoints.Length == 0)
                return;

            for (var index = 0; index < NetworkFirstPersonController.ActivePlayers.Count; index++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[index];
                if (player == null || !player.IsSpawned)
                    continue;

                var spawn = playerSpawnPoints[index % playerSpawnPoints.Length];
                // Defensive: a previous planet's spawn transform could have been destroyed
                // by an additive scene unload before the new planet's env rebound. Skip
                // dangling slots so the round can still proceed - missing players will be
                // placed when their next ServerPlaceNewPlayer/MovePlayersToShip fires.
                if (spawn == null)
                    continue;
                player.ServerTeleport(spawn.position, spawn.rotation);
                player.ServerRevive();
            }
        }

        public void ServerPlaceNewPlayer(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null)
                return;

            var spawnPoints = RoundStateUtility.IsShipPhase(Phase.Value) ? shipSpawnPoints : playerSpawnPoints;
            if (spawnPoints == null || spawnPoints.Length == 0)
                return;

            var spawn = GetSpawnForPlayer(player, spawnPoints);
            if (spawn == null) return;
            player.ServerTeleport(spawn.position, spawn.rotation);
        }

        // Teleporter-pad entry points. Distinct from ServerPlaceNewPlayer because they're
        // intended for mid-round transit between the ship and the active planet, so they
        // ignore the phase-driven spawn-point switch and explicitly target one or the other.
        public bool ServerTeleportPlayerToShip(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null) return false;
            return TeleportToSpawnPoints(player, shipSpawnPoints);
        }

        public bool ServerTeleportPlayerToPlanet(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null) return false;
            return TeleportToSpawnPoints(player, playerSpawnPoints);
        }

        private static bool TeleportToSpawnPoints(NetworkFirstPersonController player, Transform[] spawns)
        {
            if (spawns == null || spawns.Length == 0) return false;
            var spawn = GetSpawnForPlayer(player, spawns);
            if (spawn == null) return false;
            player.ServerTeleport(spawn.position, spawn.rotation);
            return true;
        }

        // Server-side fan-out for teleporter-pad effects. The flash is targeted at the
        // teleported player only (other crew members shouldn't black out when someone
        // else uses a pad), while the chirp broadcasts so everyone within earshot of the
        // pad hears it.
        public void ServerNotifyTeleporterEffect(NetworkFirstPersonController player, Vector3 padPosition)
        {
            if (!IsServer || player == null) return;

            var flashParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { player.OwnerClientId } }
            };
            TeleporterFlashClientRpc(flashParams);
            TeleporterSoundClientRpc(padPosition);
        }

        [ClientRpc]
        private void TeleporterFlashClientRpc(ClientRpcParams rpcParams = default)
        {
            LocalTeleporterFlashRequested?.Invoke();
        }

        [ClientRpc]
        private void TeleporterSoundClientRpc(Vector3 padPosition)
        {
            TeleporterAudio.PlayAt(padPosition);
        }

        private void ServerSetPhase(RoundPhase phase)
        {
            if (!IsServer)
                return;

            if (Phase.Value == phase)
                return;

            Phase.Value = phase;

            // Roll the offered next-planet picks the moment the round resolves, before the
            // host sees the success screen. Doing it here keeps the menu and the cycle button
            // in sync with whichever subset was rolled.
            if (phase == RoundPhase.Success)
            {
                ServerRollNextPlanetChoices();
                // Pre-select the first offered planet so the Travel button has a sensible
                // default even if the host doesn't touch the cycle button.
                if (NextPlanetChoiceIndices != null && NextPlanetChoiceIndices.Count > 0)
                    SelectedNextPlanetCatalogIndex.Value = NextPlanetChoiceIndices[0];
                else
                    SelectedNextPlanetCatalogIndex.Value = -1;
            }

            if (returnToShipOnRoundEnd && RoundStateUtility.IsShipPhase(phase))
            {
                ServerMovePlayersToShip(revivePlayers: true);
            }
        }

        private void ServerMovePlayersToShip(bool revivePlayers)
        {
            if (!IsServer || shipSpawnPoints == null || shipSpawnPoints.Length == 0)
                return;

            for (var index = 0; index < NetworkFirstPersonController.ActivePlayers.Count; index++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[index];
                if (player == null || !player.IsSpawned)
                    continue;

                var spawn = GetSpawnForPlayer(player, shipSpawnPoints);
                if (spawn == null) continue;
                if (revivePlayers)
                    player.ServerRevive();
                player.ServerTeleport(spawn.position, spawn.rotation);
            }
        }

        private static Transform GetSpawnForPlayer(NetworkFirstPersonController player, Transform[] spawnPoints)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;
            var startIndex = NetworkFirstPersonController.ActivePlayers.IndexOf(player);
            if (startIndex < 0) startIndex = 0;

            // Skip null/destroyed slots so a stale planet-spawn array (e.g. transforms
            // from a just-unloaded planet scene) doesn't strand players. Walk forward
            // from the player's slot and return the first live transform.
            for (var offset = 0; offset < spawnPoints.Length; offset++)
            {
                var candidate = spawnPoints[(startIndex + offset) % spawnPoints.Length];
                if (candidate != null) return candidate;
            }
            return null;
        }
    }
}
