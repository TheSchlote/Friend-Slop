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
            if (WorldTextOrientation.TryGetReadableTextRotation(
                    transform.position,
                    cameraTransform.position,
                    cameraTransform.up,
                    fallbackUp,
                    out var rotation))
            {
                transform.rotation = rotation;
            }
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
