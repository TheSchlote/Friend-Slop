using FriendSlop.Loot;
using FriendSlop.Core;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    [RequireComponent(typeof(NetworkFirstPersonController))]
    public class PlayerInteractor : NetworkBehaviour
    {
        [SerializeField] private float interactDistance = 3.2f;
        [SerializeField] private float throwImpulse = 8f;
        [SerializeField] private LayerMask interactMask = ~0;

        private NetworkFirstPersonController controller;
        private NetworkLootItem focusedLoot;

        public string CurrentPrompt { get; private set; }

        private void Awake()
        {
            controller = GetComponent<NetworkFirstPersonController>();
        }

        private void Update()
        {
            if (!IsOwner || controller.PlayerCamera == null)
            {
                return;
            }

            UpdateFocus();

            if (FriendSlop.UI.FriendSlopUI.BlocksGameplayInput)
            {
                return;
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
            {
                return;
            }

            var heldItem = controller.HeldItem;
            if (heldItem != null && heldItem.IsHeldBy(OwnerClientId))
            {
                UpdateHeldItemPose(heldItem);

                if (keyboard.qKey.wasPressedThisFrame)
                {
                    heldItem.RequestDropRpc(Vector3.zero);
                }
                else if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    heldItem.RequestDropRpc(controller.PlayerCamera.transform.forward * throwImpulse);
                }
            }

            if (keyboard.eKey.wasPressedThisFrame && focusedLoot != null)
            {
                focusedLoot.Interact(controller);
            }
        }

        private void UpdateFocus()
        {
            focusedLoot = null;
            CurrentPrompt = string.Empty;

            var cameraTransform = controller.PlayerCamera.transform;
            if (!Physics.Raycast(cameraTransform.position, cameraTransform.forward, out var hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
            {
                if (controller.HeldItem != null)
                {
                    CurrentPrompt = "Q drop | Right Mouse throw";
                }

                return;
            }

            focusedLoot = hit.collider.GetComponentInParent<NetworkLootItem>();
            if (focusedLoot == null || !focusedLoot.CanInteract(controller))
            {
                if (controller.HeldItem != null)
                {
                    CurrentPrompt = "Q drop | Right Mouse throw";
                }

                return;
            }

            CurrentPrompt = focusedLoot.GetPrompt(controller);
        }

        private void UpdateHeldItemPose(NetworkLootItem heldItem)
        {
            var cameraTransform = controller.PlayerCamera.transform;
            var distance = heldItem.CarryDistance;
            var up = SphereWorld.GetGravityUp(cameraTransform.position);
            var down = -up;
            var targetPosition = cameraTransform.position + cameraTransform.forward * distance + down * 0.15f;
            var carriedForward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
            if (carriedForward.sqrMagnitude < 0.001f)
            {
                carriedForward = Vector3.ProjectOnPlane(cameraTransform.up, up);
            }

            if (carriedForward.sqrMagnitude < 0.001f)
            {
                carriedForward = Vector3.ProjectOnPlane(transform.forward, up);
            }

            if (carriedForward.sqrMagnitude < 0.001f)
            {
                carriedForward = Vector3.Cross(up, Vector3.right);
            }

            if (carriedForward.sqrMagnitude < 0.001f)
            {
                carriedForward = Vector3.Cross(up, Vector3.forward);
            }

            var targetRotation = Quaternion.LookRotation(carriedForward.normalized, up);
            heldItem.MoveCarriedRpc(targetPosition, targetRotation);
        }
    }
}
