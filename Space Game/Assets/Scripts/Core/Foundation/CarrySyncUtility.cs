using UnityEngine;

namespace FriendSlop.Player
{
    public static class CarrySyncUtility
    {
        public static bool ShouldSendPose(
            bool hasPreviousPose,
            float currentTime,
            float nextAllowedSyncTime,
            Vector3 previousPosition,
            Quaternion previousRotation,
            Vector3 currentPosition,
            Quaternion currentRotation,
            float positionThreshold,
            float angleThreshold)
        {
            if (!hasPreviousPose)
            {
                return true;
            }

            if (currentTime < nextAllowedSyncTime)
            {
                return false;
            }

            var positionDelta = currentPosition - previousPosition;
            var positionChanged = positionThreshold <= 0f
                ? positionDelta.sqrMagnitude > 0.000001f
                : positionDelta.sqrMagnitude >= positionThreshold * positionThreshold;
            var angleDelta = Quaternion.Angle(previousRotation, currentRotation);
            var rotationChanged = angleThreshold <= 0f
                ? angleDelta > 0.001f
                : angleDelta >= angleThreshold;

            return positionChanged || rotationChanged;
        }
    }
}
