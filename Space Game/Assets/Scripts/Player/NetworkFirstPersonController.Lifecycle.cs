using FriendSlop.Round;
using UnityEngine;

namespace FriendSlop.Player
{
    public partial class NetworkFirstPersonController
    {
        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ServerForceDropHeld(Vector3.zero);
                if (_heldPlayer != null) ServerDropHeldPlayer(Vector3.zero);
                if (IsBeingCarried.Value)
                {
                    var carrier = FindByClientId(CarriedByClientId.Value);
                    carrier?.ServerDropHeldPlayer(Vector3.zero);
                }
            }

            if (IsOwner)
            {
                _health.OnValueChanged -= OnHealthChanged;
                if (_subscribedToRoundPhase)
                {
                    var rm = RoundManager.Instance;
                    if (rm != null) rm.Phase.OnValueChanged -= OnRoundPhaseChanged;
                    _subscribedToRoundPhase = false;
                }

                ReparentDetachedDeathCameraForDespawn();
            }

            ActivePlayers.Remove(this);

            if (LocalPlayer == this)
            {
                LocalPlayer = null;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            ActivePlayers.Remove(this);
        }

        private void ReparentDetachedDeathCameraForDespawn()
        {
            if (!_isDead || playerCamera == null || cameraRoot == null)
            {
                return;
            }

            var cameraTransform = playerCamera.transform;
            if (cameraTransform == cameraRoot || cameraTransform.parent != null)
            {
                return;
            }

            cameraTransform.SetParent(cameraRoot, false);
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = Quaternion.identity;
        }
    }
}
