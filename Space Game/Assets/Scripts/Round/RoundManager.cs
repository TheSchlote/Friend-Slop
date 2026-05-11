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
    public partial class RoundManager : NetworkBehaviour
    {
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
        [SerializeField] private PlanetSceneOrchestrator planetSceneOrchestrator;

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
        public NetworkVariable<int> ExpeditionsCompleted = new(0);
        // Set by survival-style objectives once the survival timer expires, giving players a
        // grace period to reach the launchpad. Replicated so HUDs can swap to "EXTRACT" copy.
        public NetworkVariable<bool> IsExtractionWindow = new(false);

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
        private int baseQuota;
        private float baseRoundLengthSeconds;
        private bool finalTierSuccessRecorded;

        public void ConfigureSpawnPoints(Transform[] spawnPoints)
        {
            playerSpawnPoints = spawnPoints;
        }

        public void ConfigureShipSpawnPoints(Transform[] spawnPoints)
        {
            shipSpawnPoints = spawnPoints;
        }

        private PlanetSceneOrchestrator EnsurePlanetSceneOrchestrator()
        {
            if (planetSceneOrchestrator == null)
                planetSceneOrchestrator = GetComponent<PlanetSceneOrchestrator>();
            if (planetSceneOrchestrator == null && !IsSpawned)
                planetSceneOrchestrator = gameObject.AddComponent<PlanetSceneOrchestrator>();
            return planetSceneOrchestrator;
        }

        private void Awake()
        {
            baseQuota = quota;
            baseRoundLengthSeconds = roundLengthSeconds;
            RoundManagerRegistry.Register(this);
            EnsurePlanetSceneOrchestrator();
            // NetworkList must exist before OnNetworkSpawn so the server's first writes replicate.
            NextPlanetChoiceIndices = new NetworkList<int>();
            // Build runtime envs for catalog planets that have no scene file (flat test
            // world etc.). Must happen before the first ApplyActivePlanetEnvironment so the
            // env is in AllEnvironments when the host opens Test Mode.
            EnsureFlatTestWorldEnvironments();
        }

        public override void OnDestroy()
        {
            RoundManagerRegistry.Unregister(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            EnsurePlanetSceneOrchestrator()?.Initialize(sceneTransitionService);

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

            if (Phase.Value == RoundPhase.Success && HasReachedFinalTier)
            {
                ServerReturnToExpeditionLobby();
                return;
            }

            ServerStartRound();
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

            if (!TryPreparePlanetForRound())
                return;

            RefreshLootCache();
            CollectedValue.Value = 0;
            TimeRemaining.Value = roundLengthSeconds;
            Quota.Value = quota;
            HasCockpit.Value = false;
            HasWings.Value = false;
            HasEngine.Value = false;
            RocketAssembled.Value = false;
            finalTierSuccessRecorded = false;
            IsExtractionWindow.Value = false;
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
                if (HasReachedFinalTier && !finalTierSuccessRecorded)
                {
                    ExpeditionsCompleted.Value++;
                    finalTierSuccessRecorded = true;
                }

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

    }
}
