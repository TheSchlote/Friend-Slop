using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    // "Player carrying" - Lethal-Company-style: a player can pick another player
    // up (alive or dead) and walk around with them. The picker is the carrier;
    // the picked is the held player. State splits across two NetworkObjects: the
    // held player owns IsBeingCarried + CarriedByClientId; the carrier owns its
    // local _heldPlayer reference. Server is authoritative for both.
    public partial class NetworkFirstPersonController
    {
        [Header("Player Carrying")]
        [SerializeField] private float carryPlayerSpeedMultiplier = 0.7f;

        public NetworkVariable<bool> IsBeingCarried = new(false);
        public NetworkVariable<ulong> CarriedByClientId = new(ulong.MaxValue);

        private NetworkFirstPersonController _heldPlayer;

        public NetworkFirstPersonController HeldPlayer => ResolveHeldPlayer();
        public bool HasHeldPlayer => ResolveHeldPlayer() != null;
        public float CarryPlayerSpeedMultiplier => carryPlayerSpeedMultiplier;

        private NetworkFirstPersonController ResolveHeldPlayer()
        {
            if (_heldPlayer != null
                && _heldPlayer.IsSpawned
                && _heldPlayer.IsBeingCarried.Value
                && _heldPlayer.CarriedByClientId.Value == OwnerClientId)
            {
                return _heldPlayer;
            }

            foreach (var p in ActivePlayers)
            {
                if (p == null || p == this || !p.IsSpawned) continue;
                if (p.IsBeingCarried.Value && p.CarriedByClientId.Value == OwnerClientId)
                {
                    _heldPlayer = p;
                    return p;
                }
            }

            _heldPlayer = null;
            return null;
        }

        // Called on the TARGET player's NetworkObject by the picker's owner.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPickupByPlayerServerRpc(RpcParams rpcParams = default)
        {
            var carrierId = rpcParams.Receive.SenderClientId;
            var carrier = FindByClientId(carrierId);
            if (carrier == null || carrier.HeldItem != null || carrier.HasHeldPlayer) return;
            if (IsBeingCarried.Value || carrier.IsDead) return;
            if (IsAncestorInCarrierChain(carrier)) return;

            IsBeingCarried.Value = true;
            CarriedByClientId.Value = carrierId;
            carrier.SetHeldPlayer(this);
            BeginCarriedClientRpc();
        }

        // Returns true if 'this' is somewhere above 'picker' in the carrier chain,
        // which would create a circular stack.
        public bool IsAncestorInCarrierChain(NetworkFirstPersonController picker)
        {
            var current = picker;
            while (current != null && current.IsBeingCarried.Value)
            {
                var above = FindByClientId(current.CarriedByClientId.Value);
                if (above == this) return true;
                if (above == current) break;
                current = above;
            }
            return false;
        }

        // Called on the CARRIER's NetworkObject by the carrier's owner.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void MoveHeldPlayerServerRpc(Vector3 position, Quaternion rotation, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || _heldPlayer == null) return;
            var carrier = FindByClientId(rpcParams.Receive.SenderClientId);
            if (carrier != null && Vector3.SqrMagnitude(position - carrier.transform.position) > 36f) return;
            _heldPlayer.ServerMoveAsCarried(position, rotation);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDropHeldPlayerServerRpc(Vector3 impulse, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || _heldPlayer == null) return;
            ServerDropHeldPlayer(impulse);
        }

        public void ServerDropHeldPlayer(Vector3 impulse)
        {
            if (!IsServer || _heldPlayer == null) return;
            var dropped = _heldPlayer;
            ClearHeldPlayer(_heldPlayer);
            dropped.ServerReleaseFromCarry(impulse);
        }

        public void ServerMoveAsCarried(Vector3 position, Quaternion rotation)
        {
            if (!IsServer || !IsBeingCarried.Value) return;
            transform.SetPositionAndRotation(position, rotation);
            if (!IsOwner) MoveAsCarriedClientRpc(position, rotation);
        }

        [ClientRpc]
        private void MoveAsCarriedClientRpc(Vector3 position, Quaternion rotation)
        {
            if (!IsOwner) return;
            if (characterController != null) characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (characterController != null) characterController.enabled = true;
        }

        [ClientRpc]
        private void BeginCarriedClientRpc()
        {
            if (!IsOwner) return;
            if (characterController != null) characterController.enabled = false;
        }

        public void ServerReleaseFromCarry(Vector3 impulse)
        {
            if (!IsServer) return;
            IsBeingCarried.Value = false;
            CarriedByClientId.Value = ulong.MaxValue;
            ReleaseFromCarryClientRpc(impulse);
        }

        [ClientRpc]
        private void ReleaseFromCarryClientRpc(Vector3 impulse)
        {
            if (!IsOwner) return;
            if (!_isDead && characterController != null) characterController.enabled = true;
            if (!_isDead)
            {
                knockbackVelocity += impulse;
                radialSpeed = 0f;
            }
        }

        public void SetHeldPlayer(NetworkFirstPersonController player) => _heldPlayer = player;

        public void ClearHeldPlayer(NetworkFirstPersonController player)
        {
            if (_heldPlayer == player) _heldPlayer = null;
        }
    }
}
