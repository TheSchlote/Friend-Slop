using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FriendSlop.Core;
using FriendSlop.Effects;
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

        [Header("Health")]
        [SerializeField] private int maxHealth = 100;

        [Header("Player Carrying")]
        [SerializeField] private float carryPlayerSpeedMultiplier = 0.7f;

        [Header("Crouch")]
        [SerializeField] private float standHeight = 0f;
        [SerializeField] private float crouchHeight = 0.9f;
        [SerializeField] private float crouchSpeedMultiplier = 0.45f;
        [SerializeField] private float crouchTransitionSpeed = 8f;

        [Header("Diagnostics")]
        [SerializeField] private bool enablePhysicsDiagnostics = true;
        [SerializeField] private float diagnosticSampleInterval = 0.2f;
        [SerializeField] private float diagnosticTouchPadding = 0.08f;
        [SerializeField] private float diagnosticNearbyRadius = 4.5f;
        [SerializeField] private LayerMask diagnosticColliderMask = ~0;

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
        private bool diagnosticRecording;
        private float nextDiagnosticSampleTime;
        private string diagnosticLogPath;
        private Transform diagnosticLaunchpad;
        private Vector2 lastMoveInput;
        private bool lastSphereGrounded;
        private bool lastWantsJump;
        private bool lastWantsSprint;
        private bool lastBlockedGameplayInput;
        private readonly Collider[] diagnosticTouchColliders = new Collider[64];
        private readonly Collider[] diagnosticNearbyColliders = new Collider[96];
        private readonly StringBuilder diagnosticBuilder = new();

        private readonly NetworkVariable<int> _health = new(100);
        private bool _isDead;
        private float _deathOverheadTimer;
        private bool _spectating;
        private int _spectatorIndex;

        public NetworkVariable<bool> IsBeingCarried = new(false);
        public NetworkVariable<ulong> CarriedByClientId = new(ulong.MaxValue);
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
        private NetworkFirstPersonController _heldPlayer;
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
        public bool IsDead => _health.Value <= 0;
        public float HealthPercent => maxHealth > 0 ? Mathf.Clamp01((float)_health.Value / maxHealth) : 0f;
        public int CurrentHealth => _health.Value;
        public int MaxHealth => maxHealth;
        public NetworkFirstPersonController HeldPlayer => ResolveHeldPlayer();
        public bool HasHeldPlayer => ResolveHeldPlayer() != null;

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
        public float CarryPlayerSpeedMultiplier => carryPlayerSpeedMultiplier;
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
        public bool IsDeadLocally => IsOwner && _isDead;
        public bool IsSpectatingLocally => IsOwner && _isDead && _spectating;
        public string SpectatorTargetLabel
        {
            get
            {
                if (!_spectating) return string.Empty;
                var alive = new List<NetworkFirstPersonController>();
                foreach (var p in ActivePlayers)
                {
                    if (p != null && p != this && p.IsSpawned && !p.IsDead)
                        alive.Add(p);
                }
                if (alive.Count == 0) return "nobody";
                var idx = ((_spectatorIndex % alive.Count) + alive.Count) % alive.Count;
                var target = alive[idx];
                var serverId = NetworkManager.Singleton != null ? NetworkManager.ServerClientId : 0UL;
                return target.OwnerClientId == serverId ? "Host" : $"Player {target.OwnerClientId}";
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

                var roundManager = RoundManager.Instance;
                if (roundManager != null)
                    roundManager.ServerPlaceNewPlayer(this);
                else
                    StartCoroutine(ServerPlaceWhenRoundManagerReady());
            }

            if (IsOwner)
            {
                LocalPlayer = this;
                ConfigureLocalPlayer(true);
                _health.OnValueChanged += OnHealthChanged;
                var savedName = UnityEngine.PlayerPrefs.GetString("PlayerName", "Player");
                SetNameServerRpc(savedName);

                var rm = RoundManager.Instance;
                if (rm != null)
                {
                    rm.Phase.OnValueChanged += OnRoundPhaseChanged;
                    _subscribedToRoundPhase = true;
                    if (rm.Phase.Value == RoundPhase.Loading)
                        StartCoroutine(WaitAndReportReady());
                    else if (rm.Phase.Value == RoundPhase.Active)
                        FriendSlopUI.Instance?.ShowLateJoinLoading();
                }
            }
            else
            {
                ConfigureLocalPlayer(false);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ServerForceDropHeld(Vector3.zero);
                if (_heldPlayer != null) ServerDropHeldPlayer(Vector3.zero);
                if (IsBeingCarried.Value)
                {
                    var carrier = FindByClientId(CarriedByClientId.Value);
                    carrier?.ServerDropHeldPlayer(Vector3.zero);
                }
            }

            if (IsOwner)
            {
                _health.OnValueChanged -= OnHealthChanged;
                if (_subscribedToRoundPhase)
                {
                    var rm = RoundManager.Instance;
                    if (rm != null) rm.Phase.OnValueChanged -= OnRoundPhaseChanged;
                    _subscribedToRoundPhase = false;
                }
            }

            ActivePlayers.Remove(this);

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
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

            HandleDiagnosticHotkeys();

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !FriendSlopUI.BlocksGameplayInput)
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

            lastBlockedGameplayInput = FriendSlopUI.BlocksGameplayInput;
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
            if (_stunTimer <= 0f && Keyboard.current != null && !FriendSlopUI.BlocksGameplayInput)
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

                var roundManager = RoundManager.Instance;
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

        private void HandleDiagnosticHotkeys()
        {
            if (!enablePhysicsDiagnostics || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.f9Key.wasPressedThisFrame)
            {
                if (diagnosticRecording)
                {
                    StopPhysicsDiagnostics();
                }
                else
                {
                    StartPhysicsDiagnostics();
                }
            }

            if (Keyboard.current.f10Key.wasPressedThisFrame)
            {
                if (!diagnosticRecording)
                {
                    StartPhysicsDiagnostics();
                }

                WritePhysicsDiagnosticSample("manual");
                Debug.Log($"Friend Slop physics diagnostic sample written to: {diagnosticLogPath}");
            }
        }

        private void StartPhysicsDiagnostics()
        {
            if (!enablePhysicsDiagnostics)
            {
                return;
            }

            var fileName = $"friend-slop-physics-{System.DateTime.Now:yyyyMMdd-HHmmss}.csv";
            diagnosticLogPath = Path.Combine(Application.persistentDataPath, fileName);
            var header = string.Join(",",
                "reason",
                "time",
                "frame",
                "position",
                "rotationEuler",
                "world",
                "surfaceDistance",
                "gravityUp",
                "playerGravity",
                "planetGravity",
                "radialSpeed",
                "controllerVelocity",
                "characterGrounded",
                "sphereGrounded",
                "upAngleError",
                "blockedInput",
                "inputAxis",
                "jumpPressedThisFrame",
                "sprintHeld",
                "keys",
                "heldItem",
                "launchpadPlanarDistance",
                "launchpadHeightDistance",
                "touchColliderCount",
                "touchColliders",
                "nearbyColliderCount",
                "nearbyColliders");

            try
            {
                File.WriteAllText(diagnosticLogPath, header + "\n");
                diagnosticRecording = true;
                nextDiagnosticSampleTime = 0f;
                Debug.Log($"Friend Slop physics diagnostics started. Press F9 to stop. Log: {diagnosticLogPath}");
            }
            catch (IOException exception)
            {
                diagnosticRecording = false;
                Debug.LogWarning($"Failed to start Friend Slop physics diagnostics: {exception.Message}");
            }
        }

        private void StopPhysicsDiagnostics()
        {
            diagnosticRecording = false;
            Debug.Log($"Friend Slop physics diagnostics stopped. Log: {diagnosticLogPath}");
        }

        private void SamplePhysicsDiagnostics(string reason)
        {
            if (!diagnosticRecording || Time.unscaledTime < nextDiagnosticSampleTime)
            {
                return;
            }

            nextDiagnosticSampleTime = Time.unscaledTime + Mathf.Max(0.02f, diagnosticSampleInterval);
            WritePhysicsDiagnosticSample(reason);
        }

        private void WritePhysicsDiagnosticSample(string reason)
        {
            if (string.IsNullOrWhiteSpace(diagnosticLogPath))
            {
                return;
            }

            try
            {
                File.AppendAllText(diagnosticLogPath, BuildPhysicsDiagnosticLine(reason) + "\n");
            }
            catch (IOException exception)
            {
                diagnosticRecording = false;
                Debug.LogWarning($"Stopped Friend Slop physics diagnostics after write failure: {exception.Message}");
            }
        }

        private string BuildPhysicsDiagnosticLine(string reason)
        {
            var world = FlatGravityVolume.TryGetContaining(transform.position, out _) ? null : SphereWorld.GetClosest(transform.position);
            var up = world != null ? world.GetUp(transform.position) : FlatGravityVolume.GetGravityUp(transform.position);
            var launchpad = GetDiagnosticLaunchpad();
            var launchpadPlanarDistance = -1f;
            var launchpadHeightDistance = -1f;

            if (launchpad != null)
            {
                var launchpadOffset = transform.position - launchpad.position;
                launchpadHeightDistance = Vector3.Dot(launchpadOffset, launchpad.up);
                launchpadPlanarDistance = Vector3.ProjectOnPlane(launchpadOffset, launchpad.up).magnitude;
            }

            var surfaceDistance = world != null ? world.GetSurfaceDistance(transform.position) : 0f;
            var planetGravity = world != null ? world.GravityAcceleration : 0f;
            var upAngleError = Vector3.Angle(transform.up, up);
            var touchColliders = FormatOverlappingControllerColliders(out var touchCount);
            var nearbyColliders = FormatNearbyColliders(out var nearbyCount);

            diagnosticBuilder.Clear();
            AppendCsv(diagnosticBuilder, reason);
            AppendCsv(diagnosticBuilder, FormatFloat(Time.time));
            AppendCsv(diagnosticBuilder, Time.frameCount.ToString(CultureInfo.InvariantCulture));
            AppendCsv(diagnosticBuilder, FormatVector(transform.position));
            AppendCsv(diagnosticBuilder, FormatVector(transform.eulerAngles));
            AppendCsv(diagnosticBuilder, world != null ? world.name : "none");
            AppendCsv(diagnosticBuilder, FormatFloat(surfaceDistance));
            AppendCsv(diagnosticBuilder, FormatVector(up));
            AppendCsv(diagnosticBuilder, FormatFloat(gravity));
            AppendCsv(diagnosticBuilder, FormatFloat(planetGravity));
            AppendCsv(diagnosticBuilder, FormatFloat(radialSpeed));
            AppendCsv(diagnosticBuilder, FormatVector(characterController != null ? characterController.velocity : Vector3.zero));
            AppendCsv(diagnosticBuilder, characterController != null && characterController.isGrounded ? "1" : "0");
            AppendCsv(diagnosticBuilder, lastSphereGrounded ? "1" : "0");
            AppendCsv(diagnosticBuilder, FormatFloat(upAngleError));
            AppendCsv(diagnosticBuilder, lastBlockedGameplayInput ? "1" : "0");
            AppendCsv(diagnosticBuilder, FormatVector2(lastMoveInput));
            AppendCsv(diagnosticBuilder, lastWantsJump ? "1" : "0");
            AppendCsv(diagnosticBuilder, lastWantsSprint ? "1" : "0");
            AppendCsv(diagnosticBuilder, FormatInputKeys());
            AppendCsv(diagnosticBuilder, HeldItem != null ? HeldItem.ItemName : "none");
            AppendCsv(diagnosticBuilder, FormatFloat(launchpadPlanarDistance));
            AppendCsv(diagnosticBuilder, FormatFloat(launchpadHeightDistance));
            AppendCsv(diagnosticBuilder, touchCount.ToString(CultureInfo.InvariantCulture));
            AppendCsv(diagnosticBuilder, touchColliders);
            AppendCsv(diagnosticBuilder, nearbyCount.ToString(CultureInfo.InvariantCulture));
            AppendCsv(diagnosticBuilder, nearbyColliders);
            return diagnosticBuilder.ToString();
        }

        private Transform GetDiagnosticLaunchpad()
        {
            if (diagnosticLaunchpad != null)
            {
                return diagnosticLaunchpad;
            }

            var launchpadObject = GameObject.Find("Part Launchpad");
            diagnosticLaunchpad = launchpadObject != null ? launchpadObject.transform : null;
            return diagnosticLaunchpad;
        }

        private string FormatOverlappingControllerColliders(out int colliderCount)
        {
            colliderCount = 0;
            if (characterController == null)
            {
                return string.Empty;
            }

            GetControllerCapsule(out var pointA, out var pointB, out var radius);
            var count = Physics.OverlapCapsuleNonAlloc(
                pointA,
                pointB,
                radius + Mathf.Max(0f, diagnosticTouchPadding),
                diagnosticTouchColliders,
                diagnosticColliderMask,
                QueryTriggerInteraction.Collide);

            return FormatColliderList(diagnosticTouchColliders, count, out colliderCount);
        }

        private string FormatNearbyColliders(out int colliderCount)
        {
            var count = Physics.OverlapSphereNonAlloc(
                transform.position,
                Mathf.Max(0.1f, diagnosticNearbyRadius),
                diagnosticNearbyColliders,
                diagnosticColliderMask,
                QueryTriggerInteraction.Collide);

            return FormatColliderList(diagnosticNearbyColliders, count, out colliderCount);
        }

        private string FormatColliderList(Collider[] colliders, int count, out int filteredCount)
        {
            filteredCount = 0;
            var builder = new StringBuilder();
            var limit = Mathf.Min(count, colliders.Length);

            for (var i = 0; i < limit; i++)
            {
                var hit = colliders[i];
                if (hit == null || hit == characterController)
                {
                    continue;
                }

                filteredCount++;
                if (builder.Length > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(hit.name);
                builder.Append("[");
                builder.Append(hit.GetType().Name);
                builder.Append(hit.isTrigger ? ":trigger" : ":solid");
                builder.Append(":layer=");
                builder.Append(LayerMask.LayerToName(hit.gameObject.layer));
                builder.Append("]");
            }

            return builder.ToString();
        }

        private void GetControllerCapsule(out Vector3 pointA, out Vector3 pointB, out float radius)
        {
            var scale = transform.lossyScale;
            radius = characterController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var height = Mathf.Max(characterController.height * Mathf.Abs(scale.y), radius * 2f);
            var center = transform.TransformPoint(characterController.center);
            var halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            pointA = center + transform.up * halfSegment;
            pointB = center - transform.up * halfSegment;
        }

        private static void AppendCsv(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append('"');
            builder.Append((value ?? string.Empty).Replace("\"", "\"\""));
            builder.Append('"');
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string FormatVector(Vector3 value)
        {
            return $"{FormatFloat(value.x)} {FormatFloat(value.y)} {FormatFloat(value.z)}";
        }

        private static string FormatVector2(Vector2 value)
        {
            return $"{FormatFloat(value.x)} {FormatFloat(value.y)}";
        }

        private static string FormatInputKeys()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
            {
                return "keyboard=none";
            }

            return string.Join(" ",
                keyboard.wKey.isPressed ? "W" : "-",
                keyboard.aKey.isPressed ? "A" : "-",
                keyboard.sKey.isPressed ? "S" : "-",
                keyboard.dKey.isPressed ? "D" : "-",
                keyboard.spaceKey.isPressed ? "Space" : "-",
                keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? "Shift" : "-",
                keyboard.eKey.isPressed ? "E" : "-",
                keyboard.qKey.isPressed ? "Q" : "-",
                mouse != null && mouse.leftButton.isPressed ? "MouseLeft" : "-",
                mouse != null && mouse.rightButton.isPressed ? "MouseRight" : "-");
        }

        private void OnRoundPhaseChanged(RoundPhase _, RoundPhase next)
        {
            if (!IsOwner) return;
            if (next == RoundPhase.Loading)
            {
                FriendSlopUI.Instance?.EnterGameplayMode();
                StartCoroutine(WaitAndReportReady());
            }
            else if (next == RoundPhase.Active)
            {
                FriendSlopUI.Instance?.EnterGameplayMode();
            }
        }

        private IEnumerator WaitAndReportReady()
        {
            yield return new WaitForSeconds(1.5f);
            if (!IsSpawned) yield break;
            var rm = RoundManager.Instance;
            if (rm != null && rm.Phase.Value == RoundPhase.Loading)
                rm.ReportLoadedServerRpc();
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous && current > 0)
                FriendSlopUI.Instance?.ShowDamageFlash();
        }

        public void ServerTakeDamage(int damage)
        {
            if (!IsServer || _health.Value <= 0) return;
            _health.Value = Mathf.Max(0, _health.Value - damage);
            var isDeath = _health.Value <= 0;
            if (isDeath)
            {
                ServerForceDropHeld(Vector3.zero);
                if (_heldPlayer != null) ServerDropHeldPlayer(Vector3.zero);
                if (IsBeingCarried.Value)
                    FindByClientId(CarriedByClientId.Value)?.ServerDropHeldPlayer(Vector3.zero);
                DieClientRpc();
            }
            if (damage > 10)
                SpawnBloodSplatterClientRpc(damage, isDeath);
        }

        [ClientRpc]
        private void SpawnBloodSplatterClientRpc(int damage, bool isDeath)
        {
            var count = Mathf.RoundToInt(Mathf.Lerp(1f, 6f, (damage - 10f) / 90f));
            if (isDeath) count += 4;
            var up = transform.up;
            var world = SphereWorld.GetClosest(transform.position);
            var groundPos = world != null ? world.GetSurfacePoint(up, 0.06f) : transform.position + up * 0.06f;
            BloodSplatter.Spawn(groundPos, up, count);
        }

        public void ServerRevive()
        {
            if (!IsServer) return;
            ServerForceDropHeld(Vector3.zero);
            if (_heldPlayer != null) ServerDropHeldPlayer(Vector3.zero);
            if (IsBeingCarried.Value)
            {
                var carrier = FindByClientId(CarriedByClientId.Value);
                carrier?.ServerDropHeldPlayer(Vector3.zero);
            }
            _health.Value = maxHealth;
            ReviveClientRpc();
        }

        [ClientRpc]
        private void DieClientRpc()
        {
            if (!IsOwner) return;
            _isDead = true;
            _spectating = false;
            _deathOverheadTimer = 5f;
            _spectatorIndex = 0;
            if (characterController != null) characterController.enabled = false;
            if (playerCamera != null) playerCamera.transform.SetParent(null);
        }

        [ClientRpc]
        private void ReviveClientRpc()
        {
            if (!IsOwner) return;
            _isDead = false;
            _spectating = false;
            _deathOverheadTimer = 0f;
            if (playerCamera != null && cameraRoot != null)
            {
                playerCamera.transform.SetParent(cameraRoot);
                playerCamera.transform.localPosition = Vector3.zero;
                playerCamera.transform.localRotation = Quaternion.identity;
            }
            if (characterController != null) characterController.enabled = true;
            knockbackVelocity = Vector3.zero;
            radialSpeed = 0f;
            _stunTimer = 0f;
            _currentTiltAngle = 0f;
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

        private void HandleDeathCamera()
        {
            if (playerCamera == null) return;

            var world = SphereWorld.GetClosest(transform.position);
            var up = world != null ? world.GetUp(transform.position) : Vector3.up;

            if (!_spectating)
            {
                _deathOverheadTimer -= Time.deltaTime;

                var overheadPos = transform.position + up * 8f;
                playerCamera.transform.position = overheadPos;
                var surfaceForward = Vector3.ProjectOnPlane(transform.forward, up);
                if (surfaceForward.sqrMagnitude < 0.001f) surfaceForward = transform.right;
                else surfaceForward.Normalize();
                playerCamera.transform.rotation = Quaternion.LookRotation(-up, surfaceForward);

                if (_deathOverheadTimer <= 0f)
                    _spectating = true;
                return;
            }

            if (Keyboard.current != null && !FriendSlopUI.BlocksGameplayInput)
            {
                if (Keyboard.current.eKey.wasPressedThisFrame) CycleSpectateTarget(1);
                if (Keyboard.current.qKey.wasPressedThisFrame) CycleSpectateTarget(-1);
            }

            var alive = new List<NetworkFirstPersonController>();
            foreach (var p in ActivePlayers)
            {
                if (p != null && p != this && p.IsSpawned && !p.IsDead)
                    alive.Add(p);
            }

            if (alive.Count > 0)
            {
                _spectatorIndex = ((_spectatorIndex % alive.Count) + alive.Count) % alive.Count;
                var target = alive[_spectatorIndex];
                if (target.PlayerCamera != null)
                {
                    playerCamera.transform.position = target.PlayerCamera.transform.position;
                    playerCamera.transform.rotation = target.PlayerCamera.transform.rotation;
                }
            }
            else
            {
                var overheadPos = transform.position + up * 8f;
                playerCamera.transform.position = overheadPos;
                var surfaceForward = Vector3.ProjectOnPlane(transform.forward, up);
                if (surfaceForward.sqrMagnitude < 0.001f) surfaceForward = transform.right;
                else surfaceForward.Normalize();
                playerCamera.transform.rotation = Quaternion.LookRotation(-up, surfaceForward);
            }
        }

        private void CycleSpectateTarget(int direction)
        {
            var aliveCount = 0;
            foreach (var p in ActivePlayers)
            {
                if (p != null && p != this && p.IsSpawned && !p.IsDead)
                    aliveCount++;
            }
            if (aliveCount == 0) return;
            _spectatorIndex = ((_spectatorIndex + direction) % aliveCount + aliveCount) % aliveCount;
        }
    }
}
