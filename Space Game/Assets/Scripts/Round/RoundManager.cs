using System.Collections.Generic;
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
        [SerializeField] private float roundLengthSeconds = 480f;
        [SerializeField] private Transform[] playerSpawnPoints;

        public NetworkVariable<RoundPhase> Phase = new(RoundPhase.Lobby);
        public NetworkVariable<float> TimeRemaining = new(0f);
        public NetworkVariable<int> CollectedValue = new(0);
        public NetworkVariable<int> Quota = new(500);
        public NetworkVariable<bool> HasCockpit = new(false);
        public NetworkVariable<bool> HasWings = new(false);
        public NetworkVariable<bool> HasEngine = new(false);
        public NetworkVariable<bool> RocketAssembled = new(false);

        private readonly List<NetworkLootItem> lootItems = new();

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
            if (IsServer)
            {
                Phase.Value = RoundPhase.Lobby;
                TimeRemaining.Value = roundLengthSeconds;
                Quota.Value = quota;
            }
        }

        private void Update()
        {
            if (!IsServer || Phase.Value != RoundPhase.Active || roundLengthSeconds <= 0f)
            {
                return;
            }

            TimeRemaining.Value = Mathf.Max(0f, TimeRemaining.Value - Time.deltaTime);
            if (TimeRemaining.Value <= 0f && CollectedValue.Value < Quota.Value)
            {
                Phase.Value = RoundPhase.Failed;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartRoundRpc(RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            ServerStartRound();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRestartRoundRpc(RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
            {
                return;
            }

            ServerStartRound();
        }

        public void ServerStartRound()
        {
            if (!IsServer)
            {
                return;
            }

            RefreshLootCache();
            CollectedValue.Value = 0;
            TimeRemaining.Value = roundLengthSeconds;
            Quota.Value = quota;
            HasCockpit.Value = false;
            HasWings.Value = false;
            HasEngine.Value = false;
            RocketAssembled.Value = false;

            foreach (var loot in lootItems)
            {
                if (loot != null)
                {
                    loot.ServerReset();
                }
            }

            RespawnPlayers();
            Phase.Value = RoundPhase.Active;
        }

        public void ServerDepositLoot(NetworkLootItem loot)
        {
            if (!IsServer || loot == null || loot.IsShipPart || Phase.Value != RoundPhase.Active || loot.IsDeposited.Value)
            {
                return;
            }

            CollectedValue.Value += loot.Value;
            loot.ServerDeposit();
        }

        public void ServerSubmitToLaunchpad(NetworkLootItem loot)
        {
            if (!IsServer || loot == null || Phase.Value != RoundPhase.Active || loot.IsDeposited.Value)
            {
                return;
            }

            if (!loot.IsShipPart)
            {
                ServerDepositLoot(loot);
                return;
            }

            switch (loot.ShipPartType)
            {
                case ShipPartType.Cockpit:
                    if (HasCockpit.Value)
                    {
                        return;
                    }

                    HasCockpit.Value = true;
                    break;
                case ShipPartType.Wings:
                    if (HasWings.Value)
                    {
                        return;
                    }

                    HasWings.Value = true;
                    break;
                case ShipPartType.Engine:
                    if (HasEngine.Value)
                    {
                        return;
                    }

                    HasEngine.Value = true;
                    break;
            }

            loot.ServerDeposit();

            if (HasCockpit.Value && HasWings.Value && HasEngine.Value)
            {
                RocketAssembled.Value = true;
                Phase.Value = RoundPhase.Success;
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

        private void RespawnPlayers()
        {
            if (playerSpawnPoints == null || playerSpawnPoints.Length == 0)
            {
                return;
            }

            for (var index = 0; index < NetworkFirstPersonController.ActivePlayers.Count; index++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[index];
                if (player == null || !player.IsSpawned)
                {
                    continue;
                }

                var spawn = playerSpawnPoints[index % playerSpawnPoints.Length];
                player.ServerTeleport(spawn.position, spawn.rotation);
            }
        }
    }
}
