using FriendSlop.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    // Owner movement, surface alignment, teleport safety, and local knockback/stun response.
    public partial class NetworkFirstPersonController
    {
        private void HandleCrouch()
        {
            if (characterController == null) return;

            var wantsCrouch = false;
            if (_stunTimer <= 0f && Keyboard.current != null && !GameplayInputState.IsBlocked)
            {
                wantsCrouch = Keyboard.current.leftCtrlKey.isPressed
                    || Keyboard.current.rightCtrlKey.isPressed
                    || Keyboard.current.cKey.isPressed;
            }

            // Stay crouched if something is overhead so we don't pop into geometry.
            if (!wantsCrouch && _currentBodyHeight < standHeight - 0.01f)
            {
                var up = SphereWorld.GetGravityUp(transform.position);
                var origin = transform.position + up * crouchHeight;
                var checkRadius = Mathf.Max(0.1f, characterController.radius * 0.92f);
                var rayDistance = Mathf.Max(0.05f, standHeight - crouchHeight + 0.1f);
                if (Physics.SphereCast(origin, checkRadius, up, out _, rayDistance, ~0, QueryTriggerInteraction.Ignore))
                    wantsCrouch = true;
            }

            var targetHeight = wantsCrouch ? crouchHeight : standHeight;
            if (!Mathf.Approximately(_currentBodyHeight, targetHeight))
            {
                _currentBodyHeight = Mathf.MoveTowards(_currentBodyHeight, targetHeight, crouchTransitionSpeed * OwnerDeltaTime);
                characterController.height = _currentBodyHeight;
                characterController.center = Vector3.up * (_currentBodyHeight * 0.5f);

                if (cameraRoot != null && standHeight > 0.01f)
                {
                    var ratio = Mathf.Clamp01(_currentBodyHeight / standHeight);
                    cameraRoot.localPosition = new Vector3(
                        _baseCameraLocalPos.x,
                        _baseCameraLocalPos.y * ratio,
                        _baseCameraLocalPos.z);
                }
            }

            var crouching = _currentBodyHeight < (standHeight + crouchHeight) * 0.5f;
            if (crouching != IsCrouching.Value)
                IsCrouching.Value = crouching;
        }

        private void HandleLook()
        {
            if (Mouse.current == null || cameraRoot == null)
            {
                return;
            }

            var delta = Mouse.current.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(transform.up, delta.x, Space.World);

            cameraPitch = Mathf.Clamp(cameraPitch - delta.y, -82f, 82f);
            cameraRoot.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);

            if (Mathf.Abs(_headPitch.Value - cameraPitch) > 0.5f)
                _headPitch.Value = cameraPitch;
        }

        private void HandleMovement()
        {
            if (characterController == null || !characterController.enabled) return;

            var world = FlatGravityVolume.TryGetContaining(transform.position, out _) ? null : SphereWorld.GetClosest(transform.position);
            var up = world != null ? world.GetUp(transform.position) : FlatGravityVolume.GetGravityUp(transform.position);
            var isGrounded = IsGroundedOnSphere(world);

            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * OwnerDeltaTime);

            if (_stunTimer > 0f)
            {
                radialSpeed = isGrounded ? 0f : Mathf.Max(radialSpeed - gravity * OwnerDeltaTime, -terminalFallSpeed);
                characterController.Move((up * radialSpeed + knockbackVelocity) * OwnerDeltaTime);
                SnapToSphereSurface(world);
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            var input = Vector2.zero;
            input.x += Keyboard.current.dKey.isPressed ? 1f : 0f;
            input.x -= Keyboard.current.aKey.isPressed ? 1f : 0f;
            input.y += Keyboard.current.wKey.isPressed ? 1f : 0f;
            input.y -= Keyboard.current.sKey.isPressed ? 1f : 0f;
            input = Vector2.ClampMagnitude(input, 1f);

            var crouching = IsCrouching.Value;
            var wantsJump = !crouching && Keyboard.current.spaceKey.wasPressedThisFrame;
            var carryingMultiplier = HeldItem != null ? HeldItem.CarrySpeedMultiplier
                : (HasHeldPlayer ? carryPlayerSpeedMultiplier : 1f);
            var crouchMultiplier = crouching ? crouchSpeedMultiplier : 1f;
            var wantsSprintInput = (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed) && !crouching;
            var isMoving = input.sqrMagnitude > 0.01f;
            var isSprinting = wantsSprintInput && isMoving && !staminaExhausted && currentStamina > 0f;
            UpdateStamina(isSprinting);
            IsSprinting = isSprinting;
            TickIceSlow(OwnerDeltaTime);
            var speed = (isSprinting ? sprintSpeed : walkSpeed) * carryingMultiplier * crouchMultiplier * IceSlowSpeedMultiplier;
            lastMoveInput = input;
            lastSphereGrounded = isGrounded;
            lastWantsJump = wantsJump;
            lastWantsSprint = isSprinting;

            var moveDirection = transform.right * input.x + transform.forward * input.y;
            moveDirection = Vector3.ProjectOnPlane(moveDirection, up);
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            var move = moveDirection * speed;
            if (isGrounded)
            {
                radialSpeed = wantsJump ? jumpVelocity : 0f;
            }
            else
            {
                radialSpeed = Mathf.Max(radialSpeed - gravity * OwnerDeltaTime, -terminalFallSpeed);
            }

            var slideDecel = world != null ? world.SurfaceSlideDecel : 0f;
            var canSlide = slideDecel > 0f && isGrounded && _stunTimer <= 0f;
            if (!canSlide)
            {
                _slipCoastVelocity = Vector3.zero;
            }
            else if (isMoving)
            {
                _slipCoastVelocity = move;
            }
            else if (_slipCoastVelocity.sqrMagnitude > 0.0001f)
            {
                _slipCoastVelocity = Vector3.MoveTowards(_slipCoastVelocity, Vector3.zero, slideDecel * OwnerDeltaTime);
                _slipCoastVelocity = Vector3.ProjectOnPlane(_slipCoastVelocity, up);
                move = _slipCoastVelocity;
            }

            characterController.Move((move + up * radialSpeed + knockbackVelocity) * OwnerDeltaTime);
            SnapToSphereSurface(world);
        }

        private void UpdateStamina(bool isSprinting)
        {
            if (isSprinting)
            {
                currentStamina = Mathf.Max(0f, currentStamina - sprintStaminaDrainPerSecond * OwnerDeltaTime);
                staminaRegenCooldown = staminaRegenDelay;
                if (currentStamina <= 0f)
                {
                    staminaExhausted = true;
                }
                return;
            }

            if (staminaRegenCooldown > 0f)
            {
                staminaRegenCooldown -= OwnerDeltaTime;
                return;
            }

            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * OwnerDeltaTime);
            if (staminaExhausted && currentStamina >= minStaminaToStartSprint)
            {
                staminaExhausted = false;
            }
        }

        private void AlignToSphereSurface()
        {
            var up = FlatGravityVolume.GetGravityUp(transform.position);
            var targetRotation = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
            if (_currentTiltAngle > 0.01f)
                targetRotation = Quaternion.AngleAxis(_currentTiltAngle, _tiltAxis) * targetRotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, surfaceAlignSpeed * OwnerDeltaTime);
        }

        private bool IsGroundedOnSphere(SphereWorld world)
        {
            if (world == null)
            {
                return characterController.isGrounded;
            }

            return world.GetSurfaceDistance(transform.position) <= groundProbeDistance && radialSpeed <= 0.25f;
        }

        private void SnapToSphereSurface(SphereWorld world)
        {
            if (world == null || radialSpeed > 0f)
            {
                return;
            }

            var surfaceDistance = world.GetSurfaceDistance(transform.position);
            if (surfaceDistance > groundProbeDistance)
            {
                return;
            }

            var up = world.GetUp(transform.position);
            var targetPosition = world.GetSurfacePoint(up, 0.04f);
            characterController.enabled = false;
            transform.position = surfaceDistance < -0.05f
                ? targetPosition
                : Vector3.MoveTowards(transform.position, targetPosition, 18f * OwnerDeltaTime);
            characterController.enabled = true;
        }

        public void ServerTeleport(Vector3 position, Quaternion rotation)
        {
            if (!IsServer)
            {
                return;
            }

            var safePosition = GetSurfaceSafeTeleportPosition(position);
            var safeRotation = GetSurfaceSafeTeleportRotation(safePosition, rotation);
            ApplyTeleport(safePosition, safeRotation);
            TeleportClientRpc(safePosition, safeRotation);
        }

        [ClientRpc]
        private void TeleportClientRpc(Vector3 position, Quaternion rotation)
        {
            ApplyTeleport(position, rotation);
        }

        private void ApplyTeleport(Vector3 position, Quaternion rotation)
        {
            var controllerWasEnabled = characterController != null && characterController.enabled;
            if (controllerWasEnabled)
                characterController.enabled = false;

            transform.SetPositionAndRotation(position, rotation);

            if (controllerWasEnabled)
                characterController.enabled = true;

            radialSpeed = 0f;
            knockbackVelocity = Vector3.zero;
            _slipCoastVelocity = Vector3.zero;
            _iceSlowTimer = 0f;
            _iceSlowFactor = 1f;
        }

        private static Vector3 GetSurfaceSafeTeleportPosition(Vector3 position)
        {
            if (FlatGravityVolume.TryGetContaining(position, out _))
                return position;

            var world = SphereWorld.GetClosest(position);
            if (world == null)
                return position;

            var up = world.GetUp(position);
            return world.GetSurfacePoint(up, 0.08f);
        }

        private static Quaternion GetSurfaceSafeTeleportRotation(Vector3 position, Quaternion requestedRotation)
        {
            if (FlatGravityVolume.TryGetContaining(position, out var volume))
            {
                var flatUp = volume.Up;
                var forward = Vector3.ProjectOnPlane(requestedRotation * Vector3.forward, flatUp);
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.ProjectOnPlane(Vector3.forward, flatUp);
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.ProjectOnPlane(Vector3.right, flatUp);
                return Quaternion.LookRotation(forward.normalized, flatUp);
            }

            var world = SphereWorld.GetClosest(position);
            if (world == null)
                return requestedRotation;

            var up = world.GetUp(position);
            return world.GetSurfaceRotation(up, requestedRotation * Vector3.forward);
        }

        [ClientRpc]
        public void KnockbackClientRpc(Vector3 impulse)
        {
            if (!IsOwner)
            {
                return;
            }

            knockbackVelocity += impulse;
        }

        [ClientRpc]
        public void StunClientRpc(float duration, Vector3 impulse)
        {
            if (!IsOwner)
            {
                return;
            }

            knockbackVelocity += impulse;
            _stunTimer = duration;

            var up = SphereWorld.GetGravityUp(transform.position);
            var knockDir = Vector3.ProjectOnPlane(impulse, up);
            _tiltAxis = knockDir.sqrMagnitude > 0.001f
                ? Vector3.Cross(up, knockDir.normalized)
                : transform.right;
            _currentTiltAngle = stunTiltAngle;
        }
    }
}
