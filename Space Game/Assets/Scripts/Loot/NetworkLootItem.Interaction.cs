using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    public partial class NetworkLootItem
    {
        private const float ServerPickupMaxDistance = 4.25f;

        public bool CanInteract(NetworkFirstPersonController player)
        {
            if (player == null || IsDeposited.Value)
            {
                return false;
            }

            if (RoundManagerRegistry.Current is { } activeRound && activeRound.Phase.Value != RoundPhase.Active)
            {
                return false;
            }

            return !IsCarried.Value || IsHeldBy(player.OwnerClientId);
        }

        public virtual string GetPrompt(NetworkFirstPersonController player)
        {
            if (IsHeldBy(player.OwnerClientId))
            {
                return $"Carrying {itemName}: Q drop | Right Mouse throw";
            }

            if (player.InventoryCount >= NetworkFirstPersonController.InventorySize)
            {
                return $"Inventory full ({itemName})";
            }

            if (IsShipPart)
            {
                return $"E pick up {itemName} ({shipPartType} part)";
            }

            return $"E pick up {itemName} (${value})";
        }

        public void Interact(NetworkFirstPersonController player)
        {
            if (player == null)
            {
                return;
            }

            if (IsHeldBy(player.OwnerClientId))
            {
                RequestDropServerRpc(Vector3.zero);
            }
            else
            {
                RequestPickupServerRpc();
            }
        }

        public bool IsHeldBy(ulong clientId)
        {
            return IsCarried.Value && CarrierClientId.Value == clientId;
        }

        public void PreviewCarriedPose(Vector3 targetPosition, Quaternion targetRotation)
        {
            if (!IsCarried.Value || IsDeposited.Value)
            {
                return;
            }

            transform.SetPositionAndRotation(targetPosition, targetRotation);
            if (body != null && body.isKinematic)
            {
                body.position = targetPosition;
                body.rotation = targetRotation;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPickupServerRpc(RpcParams rpcParams = default)
        {
            if (IsDeposited.Value || IsCarried.Value)
            {
                return;
            }

            var round = RoundManagerRegistry.Current;
            if (round == null || round.Phase.Value != RoundPhase.Active)
            {
                return;
            }

            var senderId = rpcParams.Receive.SenderClientId;
            var player = NetworkFirstPersonController.FindByClientId(senderId);
            if (player == null
                || player.HasHeldPlayer
                || player.IsDead
                || player.IsBeingCarried.Value)
            {
                return;
            }
            if (!CanServerReachForPickup(player))
            {
                return;
            }

            if (!player.TryGetFreeInventorySlot(out var slot))
            {
                return;
            }

            SlotIndex.Value = slot;
            IsCarried.Value = true;
            CarrierClientId.Value = senderId;
            ApplyPhysicsState();
            player.SetHeldItem(this);

            if (player.GetInventoryItem(player.ActiveInventorySlot.Value) == this
                || player.GetInventoryItem(player.ActiveInventorySlot.Value) == null)
            {
                player.ServerSetActiveSlot(slot);
            }
        }

        private bool CanServerReachForPickup(NetworkFirstPersonController player)
        {
            var origin = player.PlayerCamera != null
                ? player.PlayerCamera.transform.position
                : player.transform.position + player.transform.up * 1.1f;
            var target = GetServerPickupTargetPoint(origin);
            if ((target - origin).sqrMagnitude > ServerPickupMaxDistance * ServerPickupMaxDistance)
                return false;

            return HasClearPickupLine(player.transform.root, origin, transform.root, target);
        }

        // Aim at the collider point nearest the camera so flat items on curved worlds do
        // not fail line-of-sight checks against their own supporting surface.
        private Vector3 GetServerPickupTargetPoint(Vector3 origin)
        {
            var hasBounds = false;
            var bounds = default(Bounds);
            if (colliders != null)
            {
                for (var i = 0; i < colliders.Length; i++)
                {
                    var itemCollider = colliders[i];
                    if (itemCollider == null || !itemCollider.enabled) continue;
                    if (!hasBounds)
                    {
                        bounds = itemCollider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(itemCollider.bounds);
                    }
                }
            }

            return hasBounds ? bounds.ClosestPoint(origin) : transform.position;
        }

        private static bool HasClearPickupLine(Transform playerRoot, Vector3 origin, Transform itemRoot, Vector3 target)
        {
            var offset = target - origin;
            var distance = offset.magnitude;
            if (distance <= 0.01f) return true;

            var hits = Physics.RaycastAll(origin, offset / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var hitTransform = hits[i].collider != null ? hits[i].collider.transform : null;
                if (hitTransform == null) continue;
                var hitRoot = hitTransform.root;
                if (hitRoot == playerRoot || hitRoot == itemRoot) continue;
                return false;
            }

            return true;
        }

        protected static Vector3 ResolveServerAimDirection(
            NetworkFirstPersonController player,
            Vector3 requestedDirection,
            float maxAngleDegrees)
        {
            if (player == null) return Vector3.forward;

            var serverDirection = player.GetServerViewDirection();
            if (requestedDirection.sqrMagnitude < 0.001f)
                return serverDirection;

            var normalizedRequest = requestedDirection.normalized;
            return Vector3.Angle(serverDirection, normalizedRequest) <= maxAngleDegrees
                ? normalizedRequest
                : serverDirection;
        }
    }
}
