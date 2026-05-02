using UnityEngine;

namespace FriendSlop.Core
{
    public static class CarrySurfaceUtility
    {
        private const float MinimumSurfaceClearance = 0.1f;

        public static Vector3 ClampTargetAboveSurface(Vector3 targetPosition, float minimumSurfaceClearance = MinimumSurfaceClearance)
        {
            if (FlatGravityVolume.TryGetContaining(targetPosition, out _))
            {
                return targetPosition;
            }

            var world = SphereWorld.GetClosest(targetPosition);
            if (world == null)
            {
                return targetPosition;
            }

            return world.GetSurfaceDistance(targetPosition) < -minimumSurfaceClearance
                ? world.GetSurfacePoint(world.GetUp(targetPosition), minimumSurfaceClearance)
                : targetPosition;
        }
    }
}
