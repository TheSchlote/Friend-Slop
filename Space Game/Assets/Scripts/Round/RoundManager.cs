using System.Collections;
using System.Collections.Generic;
using FriendSlop.Hazards;
using FriendSlop.Loot;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public class RoundManager : NetworkBehaviour
    {
        public static RoundManager Instance { get; private set; }

        [SerializeField] private int quota = 500;
        [SerializeField] private float roundLengthSeconds = 0f;
        [SerializeField] private Transform[] playerSpawnPoints;

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

        private readonly List<NetworkLootItem> lootItems = new();
        private readonly HashSet<ulong> boardedPlayerIds = new();
        private readonly HashSet<ulong> _readyPlayerIds = new();
        private float _loadingTimeout;
        private const float LoadingTimeoutSeconds = 15f;
        private Coroutine _transitionCoroutine;
        private const float TransitionFadeSeconds = 1.0f;
        private const float TransitionHoldSeconds = 0.8f;

        public void ConfigureSpawnPoints(Transform[] spawnPoints)
        {
            playerSpawnPoints = spawnPoints;
        }

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;
            }

            CurrentPlanetCatalogIndex.OnValueChanged += OnCurrentPlanetCatalogIndexChanged;

            if (IsServer)
            {
                Phase.Value = RoundPhase.Lobby;
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
        }

        private void OnCurrentPlanetCatalogIndexChanged(int previous, int current)
        {
            ApplyActivePlanetEnvironment();
        }

        private void ApplyActivePlanetEnvironment()
        {
            var planet = CurrentPlanet;

            // Search AllEnvironments so we can find and enable planets whose roots are disabled.
            PlanetEnvironment activeEnv = null;
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env != null && env.Planet == planet) { activeEnv = env; break; }
            }

            // Toggle planet roots. For planets whose visual content lives in a separate
            // scene object (ContentRoot != gameObject), toggle that too.
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env == null) continue;
                var want = env == activeEnv;
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
                    Phase.Value = RoundPhase.Active;
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
                    Phase.Value = RoundPhase.Success;
                    return;
                }
                if (status == ObjectiveStatus.Failed)
                {
                    Phase.Value = RoundPhase.Failed;
                    return;
                }
            }
            else if (roundLengthSeconds > 0f && TimeRemaining.Value <= 0f && CollectedValue.Value < Quota.Value)
            {
                // Legacy fallback when no objective is configured anywhere.
                Phase.Value = RoundPhase.Failed;
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
            Phase.Value = RoundPhase.AllDead;
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
                var candidates = GetNextTierCandidates();
                next = candidates.Count > 0 ? candidates[0] : null;
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
            Phase.Value = RoundPhase.Transitioning;

            // Wait for clients to fade to black before swapping the planet content.
            yield return new WaitForSeconds(TransitionFadeSeconds);

            CurrentTier.Value = next.Tier;
            CurrentPlanetCatalogIndex.Value = nextIndex; // triggers OnCurrentPlanetCatalogIndexChanged on all clients
            ApplyPlanetOverrides(next);

            // Brief hold so the loading screen is readable before the round starts.
            yield return new WaitForSeconds(TransitionHoldSeconds);

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
                    Phase.Value = RoundPhase.Active;
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

            // The server (host) is always immediately ready — it initiated the round and doesn't need
            // to sync. Remote clients report via ReportLoadedServerRpc after they receive the phase change.
            if (NetworkManager != null)
            {
                _readyPlayerIds.Add(NetworkManager.ServerClientId);
                PlayersReady.Value = _readyPlayerIds.Count;
            }

            // Let the active objective seed any per-objective state (timer overrides,
            // bumped quota, etc.) before we transition into Loading.
            ActiveObjective?.ServerInitialize(this);

            RespawnPlayers();
            Phase.Value = RoundPhase.Loading;
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
                if (status == ObjectiveStatus.Success) Phase.Value = RoundPhase.Success;
                else if (status == ObjectiveStatus.Failed) Phase.Value = RoundPhase.Failed;
                return;
            }

            var connectedPlayerCount = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            if (!RoundStateUtility.IsLaunchReady(RocketAssembled.Value, PlayersBoarded.Value, connectedPlayerCount))
                return;

            Phase.Value = RoundPhase.Success;
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!IsServer) return;

            // If a client drops before reporting ready, shrink the expected count so loading isn't stuck.
            if (Phase.Value == RoundPhase.Loading && !_readyPlayerIds.Contains(clientId)
                && PlayersExpectedToLoad.Value > 0)
            {
                PlayersExpectedToLoad.Value--;
                if (_readyPlayerIds.Count >= PlayersExpectedToLoad.Value)
                    Phase.Value = RoundPhase.Active;
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

        private void RespawnPlayers()
        {
            if (playerSpawnPoints == null || playerSpawnPoints.Length == 0)
                return;

            for (var index = 0; index < NetworkFirstPersonController.ActivePlayers.Count; index++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[index];
                if (player == null || !player.IsSpawned)
                    continue;

                var spawn = playerSpawnPoints[index % playerSpawnPoints.Length];
                player.ServerTeleport(spawn.position, spawn.rotation);
                player.ServerRevive();
            }
        }

        public void ServerPlaceNewPlayer(NetworkFirstPersonController player)
        {
            if (!IsServer || player == null || playerSpawnPoints == null || playerSpawnPoints.Length == 0)
                return;

            var index = NetworkFirstPersonController.ActivePlayers.IndexOf(player);
            if (index < 0) index = 0;
            var spawn = playerSpawnPoints[index % playerSpawnPoints.Length];
            player.ServerTeleport(spawn.position, spawn.rotation);
        }
    }
}
