using System.Collections;
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Effects;
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
        // World-space tangential velocity carried frame-to-frame on slippery surfaces so
        // the player coasts after releasing input. Always Vector3.zero on non-slippery
        // worlds (SphereWorld.SurfaceSlideDecel == 0), preserving the original snap-stop.
        private Vector3 _slipCoastVelocity;


        public NetworkVariable<bool> IsCrouching = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private float _currentBodyHeight;
        private Vector3 _baseCameraLocalPos;
        private string _displayName = string.Empty;
        private bool _subscribedToRoundPhase;

        public Camera PlayerCamera => playerCamera;
        public Transform CarryAnchor => carryAnchor;
        public PlayerInteractor Interactor => interactor;
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

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                && !GameplayInputState.IsBlocked && !UseUnscaledOwnerTime)
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

            if (_stunTimer > 0f) _stunTimer -= OwnerDeltaTime;
            if (_currentTiltAngle > 0.01f) _currentTiltAngle = Mathf.MoveTowards(_currentTiltAngle, 0f, stunTiltRecoverSpeed * OwnerDeltaTime);

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
