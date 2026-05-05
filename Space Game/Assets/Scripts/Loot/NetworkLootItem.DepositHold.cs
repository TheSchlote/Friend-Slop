using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    public partial class NetworkLootItem
    {
        private const float ServerDepositHoldGraceSeconds = 0.1f;

        private ulong _depositHoldClientId = NoCarrier;
        private float _depositHoldReadyAt;
        private IItemDepositSurface _depositHoldSurface;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void BeginDepositHoldServerRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!TryResolveDepositSurface(senderId, out var surface))
            {
                ClearServerDepositHoldFor(senderId);
                return;
            }

            var holdSeconds = DepositHoldSeconds;
            if (holdSeconds <= 0f)
            {
                ClearServerDepositHoldFor(senderId);
                return;
            }

            _depositHoldClientId = senderId;
            _depositHoldSurface = surface;
            _depositHoldReadyAt = Time.time + Mathf.Max(0f, holdSeconds - ServerDepositHoldGraceSeconds);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void CancelDepositHoldServerRpc(RpcParams rpcParams = default)
        {
            ClearServerDepositHoldFor(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDepositServerRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!TryResolveDepositSurface(senderId, out var surface))
            {
                ClearServerDepositHoldFor(senderId);
                return;
            }

            if (DepositHoldSeconds > 0f)
            {
                if (_depositHoldClientId != senderId) return;
                if (_depositHoldSurface == null || !ReferenceEquals(_depositHoldSurface, surface)) return;
                if (Time.time < _depositHoldReadyAt) return;
            }

            ClearServerDepositHoldFor(senderId);
            surface.ServerSubmit(this);
        }

        private bool TryResolveDepositSurface(ulong senderId, out IItemDepositSurface surface)
        {
            surface = null;
            if (!IsHeldBy(senderId) || IsDeposited.Value) return false;

            var round = RoundManagerRegistry.Current;
            if (round == null || round.Phase.Value != RoundPhase.Active) return false;

            if (!TryGetCachedCarrier(senderId, out var carrier)) return false;

            surface = ItemDepositSurface.FindFor(carrier, this);
            return surface != null;
        }

        private void ClearServerDepositHold()
        {
            _depositHoldClientId = NoCarrier;
            _depositHoldReadyAt = 0f;
            _depositHoldSurface = null;
        }

        private void ClearServerDepositHoldFor(ulong clientId)
        {
            if (_depositHoldClientId != clientId) return;
            ClearServerDepositHold();
        }

        private void ValidateServerDepositHold()
        {
            if (_depositHoldClientId == NoCarrier) return;
            if (!TryResolveDepositSurface(_depositHoldClientId, out var surface)
                || _depositHoldSurface == null
                || !ReferenceEquals(_depositHoldSurface, surface))
            {
                ClearServerDepositHold();
            }
        }
    }
}
