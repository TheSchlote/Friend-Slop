using System.Globalization;
using System.IO;
using System.Text;
using FriendSlop.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    // Optional in-editor physics diagnostics. F9 toggles a CSV log of player physics
    // state at diagnosticSampleInterval; F10 writes a one-shot sample. Used to debug
    // sphere-gravity edge cases and stuck-on-launchpad issues. Logs are written under
    // Application.persistentDataPath; nothing is enabled by default outside the editor.
    public partial class NetworkFirstPersonController
    {
        [Header("Diagnostics")]
        [SerializeField] private bool enablePhysicsDiagnostics = true;
        [SerializeField] private float diagnosticSampleInterval = 0.2f;
        [SerializeField] private float diagnosticTouchPadding = 0.08f;
        [SerializeField] private float diagnosticNearbyRadius = 4.5f;
        [SerializeField] private LayerMask diagnosticColliderMask = ~0;

        private bool diagnosticRecording;
        private float nextDiagnosticSampleTime;
        private string diagnosticLogPath;
        private Transform diagnosticLaunchpad;
        private readonly Collider[] diagnosticTouchColliders = new Collider[64];
        private readonly Collider[] diagnosticNearbyColliders = new Collider[96];
        private readonly StringBuilder diagnosticBuilder = new();

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
    }
}
