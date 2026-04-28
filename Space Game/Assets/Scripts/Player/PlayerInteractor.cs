using FriendSlop.Loot;
using FriendSlop.Core;
using FriendSlop.Interaction;
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
        [SerializeField] private float interactRadius = 0.35f;
        [SerializeField] private float throwImpulse = 8f;
        [SerializeField] private float maxChargeDuration = 1.8f;
        [SerializeField] private float chargeThrowMultiplier = 3.5f;
        [SerializeField] private float carryPlayerDistance = 1.2f;
        [SerializeField] private float carrySyncInterval = 0.033f;
        [SerializeField] private float carrySyncPositionThreshold = 0.01f;
        [SerializeField] private float carrySyncAngleThreshold = 1f;
        [SerializeField] private LayerMask interactMask = ~0;

        private NetworkFirstPersonController controller;
        private NetworkLootItem focusedLoot;
        private NetworkFirstPersonController focusedPlayer;
        private IFriendSlopInteractable focusedInteractable;
        private float _chargeStartTime = -1f;
        private bool _isCharging;
        private bool _hasCarrySyncPose;
        private float _nextCarrySyncTime;
        private Vector3 _lastCarrySyncPosition;
        private Quaternion _lastCarrySyncRotation = Quaternion.identity;

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
            {
                ResetCarrySync();
                return;
            }

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
            HandleInventorySlotInput(keyboard);

            if (keyboard.eKey.wasPressedThisFrame)
            {
                if (focusedInteractable != null)
                    focusedInteractable.Interact(controller);
                else if (focusedPlayer != null)
                    focusedPlayer.RequestPickupByPlayerServerRpc();
            }
        }

        private void HandleInventorySlotInput(Keyboard keyboard)
        {
            // 1..4 select inventory slot. The owner has write permission on ActiveInventorySlot
            // so we set it directly — no RPC round-trip needed for hand-swapping.
            int requested = -1;
            if (keyboard.digit1Key.wasPressedThisFrame) requested = 0;
            else if (keyboard.digit2Key.wasPressedThisFrame) requested = 1;
            else if (keyboard.digit3Key.wasPressedThisFrame) requested = 2;
            else if (keyboard.digit4Key.wasPressedThisFrame) requested = 3;
            if (requested < 0) return;

            var clamped = Mathf.Clamp(requested, 0, NetworkFirstPersonController.InventorySize - 1);
            if (controller.ActiveInventorySlot.Value != clamped)
            {
                controller.ActiveInventorySlot.Value = clamped;
                ResetCarrySync();
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
                ResetCarrySync();
                return;
            }

            if (keyboard.qKey.wasPressedThisFrame)
            {
                _isCharging = false;
                ResetCarrySync();
                if (hasHeldItem) heldItem.RequestDropServerRpc(Vector3.zero);
                else controller.RequestDropHeldPlayerServerRpc(Vector3.zero);
                return;
            }

            // Boxing gloves replace the normal throw mechanic with a directional punch.
            if (hasHeldItem && heldItem is BoxingGloves gloves)
            {
                HandleBoxingGlovesInput(gloves, mouse);
                return;
            }

            // Laser gun replaces the throw mechanic with hitscan fire.
            if (hasHeldItem && heldItem is LaserGun laserGun)
            {
                HandleLaserGunInput(laserGun, mouse);
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
                ResetCarrySync();
                if (hasHeldItem) heldItem.RequestDropServerRpc(impulse);
                else controller.RequestDropHeldPlayerServerRpc(impulse);
            }
            else if (!mouse.rightButton.isPressed)
            {
                _isCharging = false;
            }
        }

        private void HandleBoxingGlovesInput(BoxingGloves gloves, Mouse mouse)
        {
            _isCharging = false;
            if (mouse == null || controller.PlayerCamera == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (!gloves.CanPunchNow) return;

            var cam = controller.PlayerCamera.transform;
            gloves.StartLocalCooldown();
            gloves.RequestPunchServerRpc(cam.position, cam.forward);
        }

        private void HandleLaserGunInput(LaserGun laserGun, Mouse mouse)
        {
            _isCharging = false;
            if (mouse == null || controller.PlayerCamera == null) return;
            if (!mouse.leftButton.isPressed) return;
            if (!laserGun.CanFireNow) return;

            var cam = controller.PlayerCamera.transform;
            laserGun.StartLocalCooldown();
            laserGun.RequestFireServerRpc(cam.position, cam.forward);
        }

        private void UpdateFocus()
        {
            focusedLoot = null;
            focusedPlayer = null;
            focusedInteractable = null;
            CurrentPrompt = string.Empty;

            var heldItem = controller.HeldItem;
            var hasHeldItem = heldItem != null && heldItem.IsHeldBy(OwnerClientId);
            var hasHeldPlayer = controller.HasHeldPlayer;

            if (!hasHeldItem && !hasHeldPlayer)
            {
                ResetCarrySync();
            }

            if (hasHeldItem) UpdateHeldItemPose(heldItem);
            if (hasHeldPlayer) UpdateHeldPlayerPose();

            var cameraTransform = controller.PlayerCamera.transform;
            // Use a SphereCast so grabbing a player or loot doesn't require pixel-perfect aim.
            // Origin is shifted forward so we don't immediately collide with our own body.
            var radius = Mathf.Max(0.05f, interactRadius);
            var origin = cameraTransform.position + cameraTransform.forward * radius;
            var castDistance = Mathf.Max(0f, interactDistance - radius);
            if (!Physics.SphereCast(origin, radius, cameraTransform.forward, out var hit,
                    castDistance, interactMask, QueryTriggerInteraction.Ignore))
            {
                UpdateCarryPrompt(hasHeldItem, hasHeldPlayer);
                return;
            }

            // Skip self-hits (own capsule, child colliders).
            if (hit.collider != null && hit.collider.transform.root == transform.root)
            {
                UpdateCarryPrompt(hasHeldItem, hasHeldPlayer);
                return;
            }

            var interactable = FindInteractable(hit.collider);
            if (interactable != null && interactable.CanInteract(controller))
            {
                focusedInteractable = interactable;
                focusedLoot = interactable as NetworkLootItem;
                CurrentPrompt = interactable.GetPrompt(controller);
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

            if (hasHeldItem && controller.HeldItem is BoxingGloves gloves)
            {
                CurrentPrompt = gloves.CanPunchNow
                    ? "Left-click punch  |  Q drop"
                    : $"[{gloves.CooldownRemaining:F1}s] recharging  |  Q drop";
                return;
            }

            if (hasHeldItem && controller.HeldItem is LaserGun laserGun)
            {
                if (laserGun.Ammo <= 0)
                    CurrentPrompt = "Laser Gun [EMPTY]  |  Q drop";
                else if (!laserGun.CanFireNow)
                    CurrentPrompt = $"[{laserGun.CooldownRemaining:F1}s] Laser Gun [{laserGun.Ammo}/{laserGun.MaxAmmo}]  |  Q drop";
                else
                    CurrentPrompt = $"Laser Gun [{laserGun.Ammo}/{laserGun.MaxAmmo}]: Hold Left-click fire  |  Q drop";
                return;
            }

            CurrentPrompt = _isCharging
                ? $"[{Mathf.RoundToInt(ChargePercent * 100f)}%] release to throw  |  Q drop"
                : "Q drop  |  Hold Right Mouse to charge throw";
        }

        private void UpdateHeldItemPose(NetworkLootItem heldItem)
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var distance = heldItem.CarryDistance;
            var up = FlatGravityVolume.GetGravityUp(cameraTransform.position);
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
            heldItem.PreviewCarriedPose(targetPosition, targetRotation);
            if (!CarrySyncUtility.ShouldSendPose(
                    _hasCarrySyncPose,
                    Time.time,
                    _nextCarrySyncTime,
                    _lastCarrySyncPosition,
                    _lastCarrySyncRotation,
                    targetPosition,
                    targetRotation,
                    carrySyncPositionThreshold,
                    carrySyncAngleThreshold))
            {
                return;
            }

            heldItem.MoveCarriedServerRpc(targetPosition, targetRotation);
            MarkCarryPoseSent(targetPosition, targetRotation);
        }

        private void UpdateHeldPlayerPose()
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var carrierPosition = controller.transform.position;
            var up = FlatGravityVolume.GetGravityUp(carrierPosition);
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(transform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.Cross(up, Vector3.right);
            if (forward.sqrMagnitude > 0.001f) forward.Normalize();

            // Park the held player in front of the carrier, lifted to ~3/4 of the carrier's
            // current body height so they hover at chest/shoulder height instead of being
            // dragged through the surface. Using CurrentBodyHeight lets crouching lower them
            // proportionally too.
            var carrierHeight = Mathf.Max(0.5f, controller.CurrentBodyHeight);
            var liftOffset = up * (carrierHeight * 0.75f);
            var targetPosition = carrierPosition + forward * carryPlayerDistance + liftOffset;

            // Match the carrier's facing so the held player keeps the same relative pose as
            // the carrier turns. We deliberately do NOT snap to the planet surface anymore -
            // the held player is in the air, so a surface snap would just re-introduce drift.
            var targetRotation = Quaternion.LookRotation(forward, up);
            if (!CarrySyncUtility.ShouldSendPose(
                    _hasCarrySyncPose,
                    Time.time,
                    _nextCarrySyncTime,
                    _lastCarrySyncPosition,
                    _lastCarrySyncRotation,
                    targetPosition,
                    targetRotation,
                    carrySyncPositionThreshold,
                    carrySyncAngleThreshold))
            {
                return;
            }

            controller.MoveHeldPlayerServerRpc(targetPosition, targetRotation);
            MarkCarryPoseSent(targetPosition, targetRotation);
        }

        private void ResetCarrySync()
        {
            _hasCarrySyncPose = false;
            _nextCarrySyncTime = 0f;
        }

        private void MarkCarryPoseSent(Vector3 targetPosition, Quaternion targetRotation)
        {
            _lastCarrySyncPosition = targetPosition;
            _lastCarrySyncRotation = targetRotation;
            _hasCarrySyncPose = true;
            _nextCarrySyncTime = Time.time + Mathf.Max(0.01f, carrySyncInterval);
        }

        private static IFriendSlopInteractable FindInteractable(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return null;
            }

            var behaviours = hitCollider.GetComponentsInParent<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IFriendSlopInteractable interactable)
                {
                    return interactable;
                }
            }

            return null;
        }
    }
}
