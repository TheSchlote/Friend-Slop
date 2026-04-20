using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Loot;
using FriendSlop.Round;
using FriendSlop.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class NetworkFirstPersonController : NetworkBehaviour
    {
        public static readonly List<NetworkFirstPersonController> ActivePlayers = new();
        public static NetworkFirstPersonController LocalPlayer { get; private set; }

        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform cameraRoot;
        [SerializeField] private Transform carryAnchor;
        [SerializeField] private Renderer[] hideForOwner;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5.2f;
        [SerializeField] private float sprintSpeed = 8f;
        [SerializeField] private float jumpVelocity = 7f;
        [SerializeField] private float gravity = 28f;
        [SerializeField] private float mouseSensitivity = 0.08f;
        [SerializeField] private float knockbackDamping = 9f;
        [SerializeField] private float surfaceAlignSpeed = 18f;
        [SerializeField] private float groundProbeDistance = 0.18f;
        [SerializeField] private float groundStickSpeed = 2.5f;
        [SerializeField] private float terminalFallSpeed = 24f;

        private CharacterController characterController;
        private PlayerInteractor interactor;
        private float radialSpeed;
        private Vector3 knockbackVelocity;
        private float cameraPitch;

        public Camera PlayerCamera => playerCamera;
        public Transform CarryAnchor => carryAnchor;
        public PlayerInteractor Interactor => interactor;
        public NetworkLootItem HeldItem { get; private set; }
        public bool HasHeldItem => HeldItem != null && HeldItem.IsCarried.Value;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            interactor = GetComponent<PlayerInteractor>();

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            if (cameraRoot == null && playerCamera != null)
            {
                cameraRoot = playerCamera.transform;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!ActivePlayers.Contains(this))
            {
                ActivePlayers.Add(this);
            }

            if (IsOwner)
            {
                LocalPlayer = this;
                ConfigureLocalPlayer(true);
            }
            else
            {
                ConfigureLocalPlayer(false);
            }
        }

        public override void OnNetworkDespawn()
        {
            ActivePlayers.Remove(this);

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
            }
        }

        private new void OnDestroy()
        {
            ActivePlayers.Remove(this);
        }

        private void ConfigureLocalPlayer(bool isLocal)
        {
            if (playerCamera != null)
            {
                playerCamera.enabled = isLocal;
                var listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = isLocal;
                }
            }

            foreach (var bodyRenderer in hideForOwner)
            {
                if (bodyRenderer != null)
                {
                    bodyRenderer.enabled = true;
                    bodyRenderer.shadowCastingMode = isLocal
                        ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }

            if (isLocal)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (!IsOwner)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                var shouldLock = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !shouldLock;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !FriendSlopUI.BlocksGameplayInput)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (FriendSlopUI.BlocksGameplayInput)
            {
                AlignToSphereSurface();
                return;
            }

            AlignToSphereSurface();
            HandleLook();
            HandleMovement();
            AlignToSphereSurface();
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
        }

        private void HandleMovement()
        {
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

            var world = SphereWorld.GetClosest(transform.position);
            var up = world != null ? world.GetUp(transform.position) : Vector3.up;
            var isGrounded = IsGroundedOnSphere(world);
            var wantsJump = Keyboard.current.spaceKey.wasPressedThisFrame;

            var carryingMultiplier = HeldItem != null ? HeldItem.CarrySpeedMultiplier : 1f;
            var wantsSprint = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            var speed = (wantsSprint ? sprintSpeed : walkSpeed) * carryingMultiplier;

            var moveDirection = transform.right * input.x + transform.forward * input.y;
            moveDirection = Vector3.ProjectOnPlane(moveDirection, up);
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            var move = moveDirection * speed;
            if (isGrounded)
            {
                radialSpeed = wantsJump ? jumpVelocity : -groundStickSpeed;
            }
            else
            {
                var gravityAcceleration = world != null ? world.GravityAcceleration : gravity;
                radialSpeed = Mathf.Max(radialSpeed - gravityAcceleration * Time.deltaTime, -terminalFallSpeed);
            }

            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);

            characterController.Move((move + up * radialSpeed + knockbackVelocity) * Time.deltaTime);
            SnapToSphereSurface(world);
        }

        private void AlignToSphereSurface()
        {
            var up = SphereWorld.GetGravityUp(transform.position);
            var targetRotation = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, surfaceAlignSpeed * Time.deltaTime);
        }

        private bool IsGroundedOnSphere(SphereWorld world)
        {
            if (world == null)
            {
                return characterController.isGrounded;
            }

            return world.GetSurfaceDistance(transform.position) <= groundProbeDistance && radialSpeed <= 0f;
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
                : Vector3.MoveTowards(transform.position, targetPosition, 18f * Time.deltaTime);
            characterController.enabled = true;
        }

        public static NetworkFirstPersonController FindByClientId(ulong clientId)
        {
            foreach (var player in ActivePlayers)
            {
                if (player != null && player.OwnerClientId == clientId)
                {
                    return player;
                }
            }

            return null;
        }

        public void SetHeldItem(NetworkLootItem item)
        {
            HeldItem = item;
        }

        public void ClearHeldItem(NetworkLootItem item)
        {
            if (HeldItem == item)
            {
                HeldItem = null;
            }
        }

        public void ServerForceDropHeld(Vector3 impulse)
        {
            if (!IsServer || HeldItem == null)
            {
                return;
            }

            HeldItem.ServerDrop(impulse);
        }

        public void ServerTeleport(Vector3 position, Quaternion rotation)
        {
            if (!IsServer)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            TeleportClientRpc(position, rotation);
        }

        [ClientRpc]
        private void TeleportClientRpc(Vector3 position, Quaternion rotation)
        {
            if (!IsOwner)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }

            characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = true;
            radialSpeed = 0f;
            knockbackVelocity = Vector3.zero;
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
    }
}
