using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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

        public Camera PlayerCamera => playerCamera;
        public Transform CarryAnchor => carryAnchor;
        public PlayerInteractor Interactor => interactor;
        public NetworkLootItem HeldItem { get; private set; }
        public bool HasHeldItem => HeldItem != null && HeldItem.IsCarried.Value;
        public float StaminaPercent => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
        public bool IsSprinting { get; private set; }
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
            currentStamina = maxStamina;
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

            HandleDiagnosticHotkeys();

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

            lastBlockedGameplayInput = FriendSlopUI.BlocksGameplayInput;
            if (lastBlockedGameplayInput)
            {
                AlignToSphereSurface();
                SamplePhysicsDiagnostics("blocked");
                return;
            }

            AlignToSphereSurface();
            HandleLook();
            HandleMovement();
            AlignToSphereSurface();
            SamplePhysicsDiagnostics("moving");
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
            var wantsSprintInput = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            var isMoving = input.sqrMagnitude > 0.01f;
            var isSprinting = wantsSprintInput && isMoving && !staminaExhausted && currentStamina > 0f;
            UpdateStamina(isSprinting);
            IsSprinting = isSprinting;
            var speed = (isSprinting ? sprintSpeed : walkSpeed) * carryingMultiplier;
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

            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);

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
            var world = SphereWorld.GetClosest(transform.position);
            var up = world != null ? world.GetUp(transform.position) : Vector3.up;
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
    }
}
