using FriendSlop.Loot;
using FriendSlop.Core;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    [RequireComponent(typeof(NetworkFirstPersonController))]
    public class PlayerInteractor : NetworkBehaviour
    {
        [SerializeField] private float interactDistance = 3.2f;
        [SerializeField] private float throwImpulse = 8f;
        [SerializeField] private float maxChargeDuration = 1.8f;
        [SerializeField] private float chargeThrowMultiplier = 3.5f;
        [SerializeField] private float carryPlayerDistance = 1.2f;
        [SerializeField] private LayerMask interactMask = ~0;

        private NetworkFirstPersonController controller;
        private NetworkLootItem focusedLoot;
        private NetworkFirstPersonController focusedPlayer;
        private float _chargeStartTime = -1f;
        private bool _isCharging;

        public string CurrentPrompt { get; private set; }
        public float ChargePercent => _isCharging ? Mathf.Clamp01((Time.time - _chargeStartTime) / maxChargeDuration) : 0f;
        public bool IsCharging => _isCharging;

        private void Awake()
        {
            controller = GetComponent<NetworkFirstPersonController>();
        }

        private void Update()
        {
            if (!IsOwner || controller.PlayerCamera == null)
                return;

            if (controller.IsDeadLocally || controller.IsBeingCarried.Value)
                return;

            UpdateFocus();

            if (FriendSlop.UI.FriendSlopUI.BlocksGameplayInput)
            {
                _isCharging = false;
                return;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
                return;

            HandleCarryInput(keyboard, mouse);

            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (focusedLoot != null)
                    focusedLoot.Interact(controller);
                else if (focusedPlayer != null)
                    focusedPlayer.RequestPickupByPlayerServerRpc();
            }
        }

        private void HandleCarryInput(Keyboard keyboard, Mouse mouse)
        {
            var heldItem = controller.HeldItem;
            var hasHeldItem = heldItem != null && heldItem.IsHeldBy(OwnerClientId);
            var hasHeldPlayer = controller.HasHeldPlayer;

            if (!hasHeldItem && !hasHeldPlayer)
            {
                _isCharging = false;
                return;
            }

            if (keyboard.qKey.wasPressedThisFrame)
            {
                _isCharging = false;
                if (hasHeldItem) heldItem.RequestDropServerRpc(Vector3.zero);
                else controller.RequestDropHeldPlayerServerRpc(Vector3.zero);
                return;
            }

            if (mouse == null) return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                _isCharging = true;
                _chargeStartTime = Time.time;
            }
            else if (_isCharging && mouse.rightButton.wasReleasedThisFrame)
            {
                var charge = Mathf.Clamp01((Time.time - _chargeStartTime) / maxChargeDuration);
                var impulse = controller.PlayerCamera.transform.forward
                    * throwImpulse * (1f + charge * (chargeThrowMultiplier - 1f));
                _isCharging = false;
                if (hasHeldItem) heldItem.RequestDropServerRpc(impulse);
                else controller.RequestDropHeldPlayerServerRpc(impulse);
            }
            else if (!mouse.rightButton.isPressed)
            {
                _isCharging = false;
            }
        }

        private void UpdateFocus()
        {
            focusedLoot = null;
            focusedPlayer = null;
            CurrentPrompt = string.Empty;

            var heldItem = controller.HeldItem;
            var hasHeldItem = heldItem != null && heldItem.IsHeldBy(OwnerClientId);
            var hasHeldPlayer = controller.HasHeldPlayer;

            if (hasHeldItem) UpdateHeldItemPose(heldItem);
            if (hasHeldPlayer) UpdateHeldPlayerPose();

            var cameraTransform = controller.PlayerCamera.transform;
            if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out var hit,
                    interactDistance, interactMask, QueryTriggerInteraction.Ignore))
            {
                UpdateCarryPrompt(hasHeldItem, hasHeldPlayer);
                return;
            }

            var loot = hit.collider.GetComponentInParent<NetworkLootItem>();
            if (loot != null && loot.CanInteract(controller))
            {
                focusedLoot = loot;
                CurrentPrompt = loot.GetPrompt(controller);
                return;
            }

            if (!hasHeldItem && !hasHeldPlayer)
            {
                var round = RoundManager.Instance;
                if (round != null && round.Phase.Value == RoundPhase.Active)
                {
                    var target = hit.collider.GetComponentInParent<NetworkFirstPersonController>();
                    if (target != null && target != controller
                        && !target.IsBeingCarried.Value
                        && !target.IsAncestorInCarrierChain(controller))
                    {
                        focusedPlayer = target;
                        var serverId = NetworkManager.Singleton != null
                            ? NetworkManager.ServerClientId : 0UL;
                        var name = target.OwnerClientId == serverId
                            ? "Host" : $"Player {target.OwnerClientId}";
                        CurrentPrompt = target.IsDead
                            ? $"E grab {name}'s body"
                            : $"E grab {name}";
                        return;
                    }
                }
            }

            UpdateCarryPrompt(hasHeldItem, hasHeldPlayer);
        }

        private void UpdateCarryPrompt(bool hasHeldItem, bool hasHeldPlayer)
        {
            if (!hasHeldItem && !hasHeldPlayer) return;
            CurrentPrompt = _isCharging
                ? $"[{Mathf.RoundToInt(ChargePercent * 100f)}%] release to throw  |  Q drop"
                : "Q drop  |  Hold Right Mouse to charge throw";
        }

        private void UpdateHeldItemPose(NetworkLootItem heldItem)
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var distance = heldItem.CarryDistance;
            var up = SphereWorld.GetGravityUp(cameraTransform.position);
            var targetPosition = cameraTransform.position + cameraTransform.forward * distance + (-up) * 0.15f;
            var carriedForward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            if (carriedForward.sqrMagnitude < 0.001f)
                carriedForward = Vector3.ProjectOnPlane(cameraTransform.up, up);
            if (carriedForward.sqrMagnitude < 0.001f)
                carriedForward = Vector3.ProjectOnPlane(transform.forward, up);
            if (carriedForward.sqrMagnitude < 0.001f)
                carriedForward = Vector3.Cross(up, Vector3.right);
            if (carriedForward.sqrMagnitude < 0.001f)
                carriedForward = Vector3.Cross(up, Vector3.forward);
            var targetRotation = Quaternion.LookRotation(carriedForward.normalized, up);
            heldItem.MoveCarriedServerRpc(targetPosition, targetRotation);
        }

        private void UpdateHeldPlayerPose()
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var up = SphereWorld.GetGravityUp(cameraTransform.position);
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(transform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.Cross(up, Vector3.right);
            if (forward.sqrMagnitude > 0.001f) forward.Normalize();

            var targetPosition = controller.transform.position + forward * carryPlayerDistance;
            var world = SphereWorld.GetClosest(targetPosition);
            if (world != null)
                targetPosition = world.GetSurfacePoint(world.GetUp(targetPosition), 0.1f);

            var targetRotation = Quaternion.LookRotation(forward, up);
            controller.MoveHeldPlayerServerRpc(targetPosition, targetRotation);
        }
    }
}
