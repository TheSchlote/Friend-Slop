using FriendSlop.Core;
using FriendSlop.Loot;
using UnityEngine;

namespace FriendSlop.Player
{
    public partial class PlayerInteractor
    {
        private void UpdateHeldItemPose(NetworkLootItem heldItem)
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var up = FlatGravityVolume.GetGravityUp(cameraTransform.position);
            Vector3 targetPosition;
            Quaternion targetRotation;

            if (heldItem is LaserGun)
            {
                // Lower-right gun hold; Euler(90) around X rotates the capsule's Y-axis
                // to point forward, so the barrel faces where the player is looking.
                targetPosition = cameraTransform.position
                    + cameraTransform.forward * 1.2f
                    + cameraTransform.right * 0.3f
                    - cameraTransform.up * 0.25f;
                targetRotation = Quaternion.LookRotation(cameraTransform.forward, up)
                    * Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                var distance = heldItem.CarryDistance;
                targetPosition = cameraTransform.position + cameraTransform.forward * distance + (-up) * 0.15f;
                var carriedForward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
                if (carriedForward.sqrMagnitude < 0.001f)
                    carriedForward = Vector3.ProjectOnPlane(cameraTransform.up, up);
                if (carriedForward.sqrMagnitude < 0.001f)
                    carriedForward = Vector3.ProjectOnPlane(transform.forward, up);
                if (carriedForward.sqrMagnitude < 0.001f)
                    carriedForward = Vector3.Cross(up, Vector3.right);
                if (carriedForward.sqrMagnitude < 0.001f)
                    carriedForward = Vector3.Cross(up, Vector3.forward);
                targetRotation = Quaternion.LookRotation(carriedForward.normalized, up);
            }

            heldItem.PreviewCarriedPose(targetPosition, targetRotation);
            if (!CarrySyncUtility.ShouldSendPose(
                    _hasCarrySyncPose,
                    Time.time,
                    _nextCarrySyncTime,
                    _lastCarrySyncPosition,
                    _lastCarrySyncRotation,
                    targetPosition,
                    targetRotation,
                    carrySyncPositionThreshold,
                    carrySyncAngleThreshold))
            {
                return;
            }

            heldItem.MoveCarriedServerRpc(targetPosition, targetRotation);
            MarkCarryPoseSent(targetPosition, targetRotation);
        }

        private void UpdateHeldPlayerPose()
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var carrierPosition = controller.transform.position;
            var up = FlatGravityVolume.GetGravityUp(carrierPosition);
            var forward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(transform.forward, up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.Cross(up, Vector3.right);
            if (forward.sqrMagnitude > 0.001f) forward.Normalize();

            // Park the held player in front of the carrier, lifted to about 3/4 of the
            // current body height so they hover at chest/shoulder height.
            var carrierHeight = Mathf.Max(0.5f, controller.CurrentBodyHeight);
            var liftOffset = up * (carrierHeight * 0.75f);
            var targetPosition = carrierPosition + forward * carryPlayerDistance + liftOffset;

            var targetRotation = Quaternion.LookRotation(forward, up);
            if (!CarrySyncUtility.ShouldSendPose(
                    _hasCarrySyncPose,
                    Time.time,
                    _nextCarrySyncTime,
                    _lastCarrySyncPosition,
                    _lastCarrySyncRotation,
                    targetPosition,
                    targetRotation,
                    carrySyncPositionThreshold,
                    carrySyncAngleThreshold))
            {
                return;
            }

            controller.MoveHeldPlayerServerRpc(targetPosition, targetRotation);
            MarkCarryPoseSent(targetPosition, targetRotation);
        }

        private void ResetCarrySync()
        {
            _hasCarrySyncPose = false;
            _nextCarrySyncTime = 0f;
        }

        private void MarkCarryPoseSent(Vector3 targetPosition, Quaternion targetRotation)
        {
            _lastCarrySyncPosition = targetPosition;
            _lastCarrySyncRotation = targetRotation;
            _hasCarrySyncPose = true;
            _nextCarrySyncTime = Time.time + Mathf.Max(0.01f, carrySyncInterval);
        }
    }
}
