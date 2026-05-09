using FriendSlop.Core;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    public partial class NetworkLootItem
    {
        private const float MaxDropImpulseMagnitude = 30f;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDropServerRpc(Vector3 impulse, RpcParams rpcParams = default)
        {
            if (!IsHeldBy(rpcParams.Receive.SenderClientId))
                return;

            var clampedImpulse = impulse.sqrMagnitude > MaxDropImpulseMagnitude * MaxDropImpulseMagnitude
                ? impulse.normalized * MaxDropImpulseMagnitude
                : impulse;
            ServerDrop(clampedImpulse);
        }

        public void ServerDrop(Vector3 impulse)
        {
            if (!IsServer || !IsCarried.Value)
            {
                return;
            }

            TryGetCachedCarrier(CarrierClientId.Value, out var carrier);
            carrier?.ClearHeldItem(this);
            ClearServerDepositHold();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            ApplyPhysicsState();

            body.AddForce(impulse, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * impulse.magnitude * 0.25f, ForceMode.VelocityChange);

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
        }

        public void ServerDeposit()
        {
            if (!IsServer || IsDeposited.Value)
            {
                return;
            }

            TryGetCachedCarrier(CarrierClientId.Value, out var carrier);
            carrier?.ClearHeldItem(this);
            ClearServerDepositHold();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = true;
            ApplyPhysicsState();
            ApplyVisibilityState();

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
        }

        public void ServerDespawnForPlanetTravel()
        {
            if (!IsServer)
            {
                return;
            }

            var networkObject = NetworkObject;
            if (networkObject == null || !networkObject.IsSpawned)
            {
                Destroy(gameObject);
                return;
            }

            TryGetCachedCarrier(CarrierClientId.Value, out var carrier);
            carrier?.ClearHeldItem(this);
            ClearServerDepositHold();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = false;

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
            networkObject.Despawn(destroy: true);
        }

        public virtual void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            var preservePosition = ShouldPreserveShipPosition();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = false;
            ClearServerDepositHold();

            body.isKinematic = true;
            if (!preservePosition)
            {
                transform.SetPositionAndRotation(spawnPosition, spawnRotation);
                body.position = spawnPosition;
                body.rotation = spawnRotation;
            }
            body.isKinematic = false;
            ClearDynamicVelocity();

            ApplyPhysicsState();
            ApplyVisibilityState();
        }

        private bool ShouldPreserveShipPosition()
        {
            if (IsCarried.Value || IsDeposited.Value) return false;
            return FlatGravityVolume.TryGetContaining(transform.position, out _);
        }
    }
}
