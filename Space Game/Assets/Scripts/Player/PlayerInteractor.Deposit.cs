using FriendSlop.Loot;
using FriendSlop.Round;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    public partial class PlayerInteractor
    {
        // Deposit-hold state. Players press F to deposit the active item; for non-objective
        // items the hold has to last DepositHoldSeconds before the RPC fires. Cancel
        // conditions: F released, player leaves the zone, item swapped, dropped, or carried.
        private float _depositHoldStartTime = -1f;
        private NetworkLootItem _depositHoldItem;
        public float DepositHoldPercent
        {
            get
            {
                if (_depositHoldItem == null || _depositHoldItem.DepositHoldSeconds <= 0f) return 0f;
                if (_depositHoldStartTime < 0f) return 0f;
                return Mathf.Clamp01((Time.time - _depositHoldStartTime) / _depositHoldItem.DepositHoldSeconds);
            }
        }
        public bool IsDepositHolding => _depositHoldStartTime >= 0f && _depositHoldItem != null;

        private void HandleDepositInput(Keyboard keyboard)
        {
            var activeItem = controller.HeldItem;
            var hasActiveItem = activeItem != null && activeItem.IsHeldBy(OwnerClientId);

            if (!hasActiveItem)
            {
                CancelDepositHold();
                return;
            }

            // Re-evaluate the surface every frame so walking out of the zone cancels
            // the hold immediately, no matter the cause (player teleport, zone disable).
            var surface = ItemDepositSurface.FindFor(controller, activeItem);
            if (surface == null)
            {
                CancelDepositHold();
                return;
            }

            // Item swap mid-hold: treat as a new deposit attempt instead of awarding the
            // old item's progress to the new one.
            if (_depositHoldItem != null && _depositHoldItem != activeItem)
                CancelDepositHold();

            var holdSeconds = activeItem.DepositHoldSeconds;

            if (keyboard.fKey.wasPressedThisFrame)
            {
                if (holdSeconds <= 0f)
                {
                    activeItem.RequestDepositServerRpc();
                    CancelDepositHold(notifyServer: false);
                    return;
                }

                activeItem.BeginDepositHoldServerRpc();
                _depositHoldStartTime = Time.time;
                _depositHoldItem = activeItem;
                return;
            }

            if (!keyboard.fKey.isPressed)
            {
                CancelDepositHold();
                return;
            }

            // F is held: tick the timer and fire when complete.
            if (_depositHoldItem == null) return;
            if (Time.time - _depositHoldStartTime < holdSeconds) return;

            activeItem.RequestDepositServerRpc();
            CancelDepositHold(notifyServer: false);
        }

        private void CancelDepositHold(bool notifyServer = true)
        {
            if (notifyServer && _depositHoldItem != null && _depositHoldItem.IsHeldBy(OwnerClientId))
                _depositHoldItem.CancelDepositHoldServerRpc();
            _depositHoldStartTime = -1f;
            _depositHoldItem = null;
        }
    }
}
