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
        [SerializeField, Min(0f)] private float maxVisibleDistance = 32f;

        private Renderer[] renderers;
        private bool renderersVisible = true;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void LateUpdate()
        {
            var cameraTransform = ResolveCameraTransform();
            if (cameraTransform == null || !IsWithinVisibleDistance(cameraTransform.position))
            {
                SetRenderersVisible(false);
                return;
            }

            SetRenderersVisible(true);
            ApplyForCamera(cameraTransform);
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
                var localPlayerCamera = LocalPlayerRegistry.CurrentCamera;
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

        private bool IsWithinVisibleDistance(Vector3 cameraPosition)
        {
            if (maxVisibleDistance <= 0f) return true;
            return (transform.position - cameraPosition).sqrMagnitude <= maxVisibleDistance * maxVisibleDistance;
        }

        private void SetRenderersVisible(bool visible)
        {
            if (renderersVisible == visible) return;
            renderersVisible = visible;

            if (renderers == null) return;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = visible;
            }
        }
    }
}
