using System.Collections;
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Effects;
using FriendSlop.Loot;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    [RequireComponent(typeof(CharacterController))]
    public partial class NetworkFirstPersonController : NetworkBehaviour
    {
        public static readonly List<NetworkFirstPersonController> ActivePlayers = new();

        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform cameraRoot;
        [SerializeField] private Transform carryAnchor;
        [SerializeField] private Renderer[] hideForOwner;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5.2f;
        [SerializeField] private float sprintSpeed = 8f;
        [SerializeField] private float jumpVelocity = 7.2f;
        [SerializeField] private float gravity = 14f;
        [SerializeField] private float mouseSensitivity = 0.08f;
        [SerializeField] private float knockbackDamping = 9f;
        [SerializeField] private float surfaceAlignSpeed = 14f;
        [SerializeField] private float groundProbeDistance = 0.22f;
        [SerializeField] private float terminalFallSpeed = 18f;

        [Header("Stamina")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float sprintStaminaDrainPerSecond = 25f;
        [SerializeField] private float staminaRegenPerSecond = 18f;
        [SerializeField] private float staminaRegenDelay = 0.6f;
        [SerializeField] private float minStaminaToStartSprint = 20f;

        [Header("Stun")]
        [SerializeField] private float stunTiltAngle = 42f;
        [SerializeField] private float stunTiltRecoverSpeed = 80f;

        [Header("Crouch")]
        [SerializeField] private float standHeight = 0f;
        [SerializeField] private float crouchHeight = 0.9f;
        [SerializeField] private float crouchSpeedMultiplier = 0.45f;
        [SerializeField] private float crouchTransitionSpeed = 8f;

        private CharacterController characterController;
        private PlayerInteractor interactor;
        private float radialSpeed;
        private float currentStamina;
        private float staminaRegenCooldown;
        private bool staminaExhausted;
        private Vector3 knockbackVelocity;
        private float cameraPitch;
        private float _stunTimer;
        private float _currentTiltAngle;
        private Vector3 _tiltAxis;
        private Vector2 lastMoveInput;
        private bool lastSphereGrounded;
        private bool lastWantsJump;
        private bool lastWantsSprint;
        private bool lastBlockedGameplayInput;


        public NetworkVariable<bool> IsCrouching = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        public const int InventorySize = 4;
        public NetworkVariable<int> ActiveInventorySlot = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private readonly NetworkLootItem[] inventory = new NetworkLootItem[InventorySize];
        private float _currentBodyHeight;
        private Vector3 _baseCameraLocalPos;
        private string _displayName = string.Empty;
        private bool _subscribedToRoundPhase;

        public Camera PlayerCamera => playerCamera;
        public Transform CarryAnchor => carryAnchor;
        public PlayerInteractor Interactor => interactor;
        public NetworkLootItem HeldItem
        {
            get
            {
                var slot = Mathf.Clamp(ActiveInventorySlot.Value, 0, InventorySize - 1);
                return inventory[slot];
            }
        }
        public bool HasHeldItem => HeldItem != null && HeldItem.IsCarried.Value;
        public NetworkLootItem GetInventoryItem(int slot)
        {
            if (slot < 0 || slot >= InventorySize) return null;
            return inventory[slot];
        }
        public int InventoryCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < InventorySize; i++) if (inventory[i] != null) count++;
                return count;
            }
        }
        public float StaminaPercent => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
        public bool IsSprinting { get; private set; }
        public float StandHeight => standHeight;
        public float CrouchHeight => crouchHeight;
        public float CurrentBodyHeight => IsCrouching.Value ? crouchHeight : standHeight;
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_displayName)) return _displayName;
                var serverId = NetworkManager.Singleton != null ? NetworkManager.ServerClientId : 0UL;
                return OwnerClientId == serverId ? "Host" : $"Player {OwnerClientId}";
            }
        }
        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            interactor = GetComponent<PlayerInteractor>();

            if (playerCamera == null)
                playerCamera = GetComponentInChildren<Camera>(true);
            if (cameraRoot == null && playerCamera != null)
                cameraRoot = playerCamera.transform;

            currentStamina = maxStamina;

            if (characterController != null)
            {
                if (standHeight <= 0f) standHeight = characterController.height;
                _currentBodyHeight = characterController.height;
            }
            else
            {
                if (standHeight <= 0f) standHeight = 1.8f;
                _currentBodyHeight = standHeight;
            }
            if (cameraRoot != null) _baseCameraLocalPos = cameraRoot.localPosition;

            ReparentHeadBonesToCameraRoot();

            if (GetComponent<PlayerNameplate>() == null)
                gameObject.AddComponent<PlayerNameplate>();
        }

        public override void OnNetworkSpawn()
        {
            if (!ActivePlayers.Contains(this))
            {
                ActivePlayers.Add(this);
            }

            if (IsServer)
            {
                _health.Value = maxHealth;

                // Send all existing players' names to this newly connected client.
                var newClientId = OwnerClientId;
                var sendParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { newClientId } }
                };
                foreach (var other in ActivePlayers)
                {
                    if (other != null && other != this && other.IsSpawned && !string.IsNullOrEmpty(other._displayName))
                        other.SyncNameToClientRpc(other._displayName, sendParams);
                }

                var roundManager = RoundManagerRegistry.Current;
                if (roundManager != null)
                    roundManager.ServerPlaceNewPlayer(this);
                else
                    StartCoroutine(ServerPlaceWhenRoundManagerReady());
            }

            if (IsOwner)
            {
                LocalPlayerRegistry.Register(this);
                ConfigureLocalPlayer(true);
                _health.OnValueChanged += OnHealthChanged;
                var savedName = UnityEngine.PlayerPrefs.GetString("PlayerName", "Player");
                SetNameServerRpc(savedName);

                var rm = RoundManagerRegistry.Current;
                if (rm != null)
                {
                    rm.Phase.OnValueChanged += OnRoundPhaseChanged;
                    _subscribedToRoundPhase = true;
                    if (rm.Phase.Value == RoundPhase.Loading)
                        StartCoroutine(WaitAndReportReady());
                    else if (rm.Phase.Value == RoundPhase.Active)
                        LocalPlayerRegistry.NotifyJoinedActiveRound();
                }
            }
            else
            {
                ConfigureLocalPlayer(false);
            }
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

            HandleDiagnosticHotkeys();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !GameplayInputState.IsBlocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (_isDead)
            {
                HandleDeathCamera();
                return;
            }

            if (IsBeingCarried.Value)
            {
                AlignToSphereSurface();
                if (_stunTimer <= 0f) HandleLook();
                return;
            }

            if (_stunTimer > 0f) _stunTimer -= Time.deltaTime;
            if (_currentTiltAngle > 0.01f) _currentTiltAngle = Mathf.MoveTowards(_currentTiltAngle, 0f, stunTiltRecoverSpeed * Time.deltaTime);

            lastBlockedGameplayInput = GameplayInputState.IsBlocked;
            if (lastBlockedGameplayInput)
            {
                AlignToSphereSurface();
                SamplePhysicsDiagnostics("blocked");
                return;
            }

            AlignToSphereSurface();
            if (_stunTimer <= 0f)
            {
                HandleLook();
            }
            HandleCrouch();
            HandleMovement();
            AlignToSphereSurface();
            SamplePhysicsDiagnostics("moving");
        }

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
                _currentBodyHeight = Mathf.MoveTowards(_currentBodyHeight, targetHeight, crouchTransitionSpeed * Time.deltaTime);
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

            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);

            if (_stunTimer > 0f)
            {
                radialSpeed = isGrounded ? 0f : Mathf.Max(radialSpeed - gravity * Time.deltaTime, -terminalFallSpeed);
                characterController.Move((up * radialSpeed + knockbackVelocity) * Time.deltaTime);
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
            var speed = (isSprinting ? sprintSpeed : walkSpeed) * carryingMultiplier * crouchMultiplier;
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
                radialSpeed = Mathf.Max(radialSpeed - gravity * Time.deltaTime, -terminalFallSpeed);
            }

            characterController.Move((move + up * radialSpeed + knockbackVelocity) * Time.deltaTime);
            SnapToSphereSurface(world);
        }
        private void UpdateStamina(bool isSprinting)
        {
            if (isSprinting)
            {
                currentStamina = Mathf.Max(0f, currentStamina - sprintStaminaDrainPerSecond * Time.deltaTime);
                staminaRegenCooldown = staminaRegenDelay;
                if (currentStamina <= 0f)
                {
                    staminaExhausted = true;
                }
                return;
            }

            if (staminaRegenCooldown > 0f)
            {
                staminaRegenCooldown -= Time.deltaTime;
                return;
            }

            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
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
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, surfaceAlignSpeed * Time.deltaTime);
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

        // Called from NetworkLootItem.OnCarrierChanged on every client when an item's
        // carrier becomes this player. SlotIndex on the item is authoritative.
        public void SetHeldItem(NetworkLootItem item)
        {
            if (item == null) return;
            var slot = item.SlotIndex.Value;
            if (slot < 0 || slot >= InventorySize)
            {
                // Backward-compat path for callers that don't go through pickup: drop into
                // the first empty slot.
                if (!TryGetFreeInventorySlot(out slot)) return;
            }
            inventory[slot] = item;
        }

        public void ClearHeldItem(NetworkLootItem item)
        {
            if (item == null) return;
            for (var i = 0; i < InventorySize; i++)
            {
                if (inventory[i] == item) inventory[i] = null;
            }
        }

        public bool TryGetFreeInventorySlot(out int slot)
        {
            for (var i = 0; i < InventorySize; i++)
            {
                if (inventory[i] == null) { slot = i; return true; }
            }
            slot = -1;
            return false;
        }

        public void ServerSetActiveSlot(int slot)
        {
            if (!IsServer) return;
            ActiveInventorySlot.Value = Mathf.Clamp(slot, 0, InventorySize - 1);
        }

        // After a drop/deposit clears the active slot, jump to the next slot that still has
        // an item so the player keeps holding something instead of staring at empty hands.
        public void ServerCycleToNonEmptySlotIfActiveCleared()
        {
            if (!IsServer) return;
            var current = Mathf.Clamp(ActiveInventorySlot.Value, 0, InventorySize - 1);
            if (inventory[current] != null) return;
            for (var step = 1; step <= InventorySize; step++)
            {
                var candidate = (current + step) % InventorySize;
                if (inventory[candidate] != null)
                {
                    ActiveInventorySlot.Value = candidate;
                    return;
                }
            }
        }

        public void ServerForceDropHeld(Vector3 impulse)
        {
            if (!IsServer) return;
            for (var i = 0; i < InventorySize; i++)
            {
                var item = inventory[i];
                if (item != null) item.ServerDrop(impulse);
            }
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

        private IEnumerator ServerPlaceWhenRoundManagerReady()
        {
            for (var frame = 0; frame < 120; frame++)
            {
                if (!IsSpawned)
                    yield break;

                var roundManager = RoundManagerRegistry.Current;
                if (roundManager != null)
                {
                    roundManager.ServerPlaceNewPlayer(this);
                    yield break;
                }

                yield return null;
            }
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

        private void OnRoundPhaseChanged(RoundPhase _, RoundPhase next)
        {
            if (!IsOwner) return;
            if (next == RoundPhase.Loading)
            {
                StartCoroutine(WaitAndReportReady());
            }
            // UI cursor + menu state for Loading/Active transitions is owned by FriendSlopUI,
            // which subscribes to RoundManager.Phase.OnValueChanged itself. This class no
            // longer reaches into the UI layer.
        }

        private IEnumerator WaitAndReportReady()
        {
            yield return new WaitForSeconds(1.5f);
            if (!IsSpawned) yield break;
            var rm = RoundManagerRegistry.Current;
            if (rm != null && rm.Phase.Value == RoundPhase.Loading)
                rm.ReportLoadedServerRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetNameServerRpc(string name, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            name = name.Trim();
            if (name.Length < 2) name = "Player";
            if (name.Length > 24) name = name[..24];
            _displayName = name;
            SyncNameClientRpc(name);
        }

        [ClientRpc]
        private void SyncNameClientRpc(string name)
        {
            _displayName = name;
        }

        [ClientRpc]
        private void SyncNameToClientRpc(string name, ClientRpcParams rpcParams = default)
        {
            _displayName = name;
        }

    }
}
