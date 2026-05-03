using FriendSlop.Interaction;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Ship
{
    public enum ShipStationRole : byte
    {
        Pilot = 0,
        HolographicBoard = 1,
        MiniGame = 2,
        Customization = 3,
        Utility = 4
    }

    [RequireComponent(typeof(NetworkObject))]
    public class ShipStation : NetworkBehaviour, IFriendSlopInteractable
    {
        public const ulong NoOccupant = ulong.MaxValue;

        [SerializeField] private string displayName = "Ship Station";
        [SerializeField] private ShipStationRole role = ShipStationRole.Utility;
        [SerializeField] private bool requiresShipPhase = true;

        public NetworkVariable<ulong> OccupantClientId = new(NoOccupant);

        public ShipStationRole Role => role;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public bool IsOccupied => OccupantClientId.Value != NoOccupant;

        public bool CanInteract(NetworkFirstPersonController player)
        {
            if (player == null || player.IsDead || player.IsBeingCarried.Value)
            {
                return false;
            }

            if (requiresShipPhase && !RoundStateUtility.IsShipPhase(
                    RoundManagerRegistry.CurrentPhaseOrDefault()))
            {
                return false;
            }

            return !IsOccupied || OccupantClientId.Value == player.OwnerClientId;
        }

        public string GetPrompt(NetworkFirstPersonController player)
        {
            if (CanStartRoundFromStation())
            {
                return $"E start round from {DisplayName}";
            }

            if (player != null && OccupantClientId.Value == player.OwnerClientId)
            {
                return $"E leave {DisplayName}";
            }

            if (IsOccupied)
            {
                return $"{DisplayName} in use";
            }

            return role == ShipStationRole.Pilot
                ? $"E claim {DisplayName}"
                : $"E use {DisplayName}";
        }

        public void Interact(NetworkFirstPersonController player)
        {
            if (player == null)
            {
                return;
            }

            RequestUseServerRpc(player.OwnerClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestUseServerRpc(ulong requestedClientId, RpcParams rpcParams = default)
        {
            if (requestedClientId != rpcParams.Receive.SenderClientId)
            {
                return;
            }

            if (requiresShipPhase && !RoundStateUtility.IsShipPhase(
                    RoundManagerRegistry.CurrentPhaseOrDefault()))
            {
                return;
            }

            if (CanStartRoundFromStation())
            {
                var roundManager = RoundManagerRegistry.Current;
                if (roundManager != null && roundManager.IsServer)
                    roundManager.ServerStartRound();
                return;
            }

            if (OccupantClientId.Value == requestedClientId)
            {
                OccupantClientId.Value = NoOccupant;
                return;
            }

            if (OccupantClientId.Value == NoOccupant)
            {
                OccupantClientId.Value = requestedClientId;
            }
        }

        private bool CanStartRoundFromStation()
        {
            return role == ShipStationRole.Pilot
                   && RoundManagerRegistry.IsCurrentPhase(RoundPhase.Lobby);
        }
    }
}
