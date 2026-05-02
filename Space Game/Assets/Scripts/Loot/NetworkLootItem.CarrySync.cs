using FriendSlop.Core;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    public partial class NetworkLootItem
    {
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void MoveCarriedServerRpc(Vector3 targetPosition, Quaternion targetRotation, RpcParams rpcParams = default)
        {
            if (!IsHeldBy(rpcParams.Receive.SenderClientId) || IsDeposited.Value)
            {
                return;
            }

            var player = NetworkFirstPersonController.FindByClientId(rpcParams.Receive.SenderClientId);
            if (player != null && Vector3.SqrMagnitude(targetPosition - player.transform.position) > 16f)
            {
                targetPosition = player.transform.position + player.transform.forward * carryDistance + player.transform.up * 1.2f;
            }

            targetPosition = CarrySurfaceUtility.ClampTargetAboveSurface(targetPosition);

            body.position = targetPosition;
            body.rotation = targetRotation;
            ClearDynamicVelocity();
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }
    }
}
