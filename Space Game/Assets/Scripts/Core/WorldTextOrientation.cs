using UnityEngine;

namespace FriendSlop.Core
{
    // Pure-math helper for orienting a TextMesh so its glyphs face a camera.
    // Lives in Core (no Unity-scene dependency beyond UnityEngine.Vector3/Quaternion)
    // so callers in any layer can use it without dragging in UI types.
    public static class WorldTextOrientation
    {
        // TextMesh glyphs are readable from local -Z, so the rotation points the
        // local forward axis AWAY from the viewer. Returns false (and identity)
        // when text and camera are coincident.
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
    }
}
