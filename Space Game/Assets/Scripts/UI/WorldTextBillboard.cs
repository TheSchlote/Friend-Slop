using FriendSlop.Core;
using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldTextBillboard : MonoBehaviour
    {
        [SerializeField] private bool preferLocalPlayerCamera = true;
        [SerializeField] private bool fallbackToMainCamera = true;

        private void LateUpdate()
        {
            var cameraTransform = ResolveCameraTransform();
            if (cameraTransform != null)
            {
                ApplyForCamera(cameraTransform);
            }
        }

        public void ApplyForCamera(Transform cameraTransform)
        {
            if (cameraTransform == null)
            {
                return;
            }

            var fallbackUp = SphereWorld.GetGravityUp(transform.position);
            if (TryGetReadableTextRotation(
                    transform.position,
                    cameraTransform.position,
                    cameraTransform.up,
                    fallbackUp,
                    out var rotation))
            {
                transform.rotation = rotation;
            }
        }

        public static bool TryGetReadableTextRotation(
            Vector3 textPosition,
            Vector3 cameraPosition,
            Vector3 cameraUp,
            Vector3 fallbackUp,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            var toCamera = cameraPosition - textPosition;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            // TextMesh glyphs are readable from local -Z, so forward points away from the viewer.
            var forward = -toCamera.normalized;
            var up = Vector3.ProjectOnPlane(cameraUp, forward);
            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.ProjectOnPlane(fallbackUp, forward);
            }

            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, forward);
            }

            if (up.sqrMagnitude < 0.0001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.right, forward);
            }

            rotation = Quaternion.LookRotation(forward, up.normalized);
            return true;
        }

        private Transform ResolveCameraTransform()
        {
            if (preferLocalPlayerCamera)
            {
                var localPlayerCamera = NetworkFirstPersonController.LocalPlayer != null
                    ? NetworkFirstPersonController.LocalPlayer.PlayerCamera
                    : null;
                if (localPlayerCamera != null && localPlayerCamera.isActiveAndEnabled)
                {
                    return localPlayerCamera.transform;
                }
            }

            if (!fallbackToMainCamera)
            {
                return null;
            }

            var mainCamera = Camera.main;
            return mainCamera != null && mainCamera.isActiveAndEnabled ? mainCamera.transform : null;
        }
    }
}
