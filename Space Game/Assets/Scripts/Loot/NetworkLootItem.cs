using FriendSlop.Core;
using FriendSlop.Interaction;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public class NetworkLootItem : NetworkBehaviour, IFriendSlopInteractable
    {
        private const ulong NoCarrier = ulong.MaxValue;

        [SerializeField] private string itemName = "Weird Junk";
        [SerializeField] private int value = 50;
        [SerializeField] private float carrySpeedMultiplier = 0.85f;
        [SerializeField] private float carryDistance = 2.1f;
        [SerializeField] private ShipPartType shipPartType = ShipPartType.None;

        private Rigidbody body;
        private SphericalRigidbodyGravity sphericalGravity;
        private Collider[] colliders;
        private Renderer[] renderers;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        // Declaration order matters: NGO deserializes NetworkVariables in this order, and
        // OnCarrierChanged reads SlotIndex.Value when wiring the carrier's inventory cache.
        // Keeping SlotIndex above CarrierClientId guarantees the slot is current by the
        // time the carrier-change handler fires on remote clients.
        public NetworkVariable<bool> IsCarried = new(false);
        // -1 = not in any inventory. Set by the server when the item is picked up.
        public NetworkVariable<int> SlotIndex = new(-1);
        public NetworkVariable<ulong> CarrierClientId = new(NoCarrier);
        public NetworkVariable<bool> IsDeposited = new(false);

        private NetworkFirstPersonController subscribedCarrier;

        public string ItemName => itemName;
        public int Value => value;
        public float CarrySpeedMultiplier => carrySpeedMultiplier;
        public float CarryDistance => carryDistance;
        public ShipPartType ShipPartType => shipPartType;
        public bool IsShipPart => shipPartType != ShipPartType.None;

        public void ServerSetSpawnPose(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            if (body == null)
            {
                return;
            }

            body.position = position;
            body.rotation = rotation;
            ClearDynamicVelocity();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            sphericalGravity = GetComponent<SphericalRigidbodyGravity>();
            body.useGravity = false;
            body.isKinematic = true;
            if (sphericalGravity != null)
            {
                sphericalGravity.enabled = false;
            }

            colliders = GetComponentsInChildren<Collider>();
            renderers = GetComponentsInChildren<Renderer>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        public override void OnNetworkSpawn()
        {
            CarrierClientId.OnValueChanged += OnCarrierChanged;
            IsCarried.OnValueChanged += OnCarriedChanged;
            IsDeposited.OnValueChanged += OnDepositedChanged;
            SlotIndex.OnValueChanged += OnSlotIndexChanged;

            ApplyPhysicsState();
            ApplyVisibilityState();
            ApplyColliderState();
            OnCarrierChanged(NoCarrier, CarrierClientId.Value);
        }

        public override void OnNetworkDespawn()
        {
            CarrierClientId.OnValueChanged -= OnCarrierChanged;
            IsCarried.OnValueChanged -= OnCarriedChanged;
            IsDeposited.OnValueChanged -= OnDepositedChanged;
            SlotIndex.OnValueChanged -= OnSlotIndexChanged;
            UnsubscribeFromCarrierActiveSlot();
        }

        public bool CanInteract(NetworkFirstPersonController player)
        {
            if (player == null || IsDeposited.Value)
            {
                return false;
            }

            if (RoundManager.Instance != null && RoundManager.Instance.Phase.Value != RoundPhase.Active)
            {
                return false;
            }

            return !IsCarried.Value || IsHeldBy(player.OwnerClientId);
        }

        public virtual string GetPrompt(NetworkFirstPersonController player)
        {
            if (IsHeldBy(player.OwnerClientId))
            {
                return $"Carrying {itemName}: Q drop | Right Mouse throw";
            }

            if (player.InventoryCount >= NetworkFirstPersonController.InventorySize)
            {
                return $"Inventory full ({itemName})";
            }

            if (IsShipPart)
            {
                return $"E pick up {itemName} ({shipPartType} part)";
            }

            return $"E pick up {itemName} (${value})";
        }

        public void Interact(NetworkFirstPersonController player)
        {
            if (player == null)
            {
                return;
            }

            if (IsHeldBy(player.OwnerClientId))
            {
                RequestDropServerRpc(Vector3.zero);
            }
            else
            {
                RequestPickupServerRpc();
            }
        }

        public bool IsHeldBy(ulong clientId)
        {
            return IsCarried.Value && CarrierClientId.Value == clientId;
        }

        public void PreviewCarriedPose(Vector3 targetPosition, Quaternion targetRotation)
        {
            if (!IsCarried.Value || IsDeposited.Value)
            {
                return;
            }

            transform.SetPositionAndRotation(targetPosition, targetRotation);
            if (body != null && body.isKinematic)
            {
                body.position = targetPosition;
                body.rotation = targetRotation;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPickupServerRpc(RpcParams rpcParams = default)
        {
            if (IsDeposited.Value || IsCarried.Value)
            {
                return;
            }

            var round = RoundManager.Instance;
            if (round == null || round.Phase.Value != RoundPhase.Active)
            {
                return;
            }

            var senderId = rpcParams.Receive.SenderClientId;
            var player = NetworkFirstPersonController.FindByClientId(senderId);
            if (player == null
                || player.HasHeldPlayer
                || player.IsDead
                || player.IsBeingCarried.Value)
            {
                return;
            }

            // Multi-slot inventory: take the first empty slot, or refuse pickup if full.
            if (!player.TryGetFreeInventorySlot(out var slot))
            {
                return;
            }

            // Order matters: SlotIndex must be set BEFORE CarrierClientId so the
            // carrier-change handler reads the correct slot when wiring the inventory cache.
            SlotIndex.Value = slot;
            IsCarried.Value = true;
            CarrierClientId.Value = senderId;
            ApplyPhysicsState();
            player.SetHeldItem(this);
            // If the active slot was empty, switch to the freshly picked slot so the
            // player sees what they just grabbed.
            if (player.GetInventoryItem(player.ActiveInventorySlot.Value) == this
                || player.GetInventoryItem(player.ActiveInventorySlot.Value) == null)
            {
                player.ServerSetActiveSlot(slot);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void MoveCarriedServerRpc(Vector3 targetPosition, Quaternion targetRotation, RpcParams rpcParams = default)
        {
            if (!IsHeldBy(rpcParams.Receive.SenderClientId) || IsDeposited.Value)
            {
                return;
            }

            var player = NetworkFirstPersonController.FindByClientId(rpcParams.Receive.SenderClientId);
            if (player != null && Vector3.SqrMagnitude(targetPosition - player.transform.position) > 16f)
            {
                targetPosition = player.transform.position + player.transform.forward * carryDistance + player.transform.up * 1.2f;
            }

            body.position = targetPosition;
            body.rotation = targetRotation;
            ClearDynamicVelocity();
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDropServerRpc(Vector3 impulse, RpcParams rpcParams = default)
        {
            if (!IsHeldBy(rpcParams.Receive.SenderClientId))
            {
                return;
            }

            ServerDrop(impulse);
        }

        public void ServerDrop(Vector3 impulse)
        {
            if (!IsServer || !IsCarried.Value)
            {
                return;
            }

            var carrier = NetworkFirstPersonController.FindByClientId(CarrierClientId.Value);
            carrier?.ClearHeldItem(this);

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            ApplyPhysicsState();

            body.AddForce(impulse, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * impulse.magnitude * 0.25f, ForceMode.VelocityChange);

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
        }

        public void ServerDeposit()
        {
            if (!IsServer || IsDeposited.Value)
            {
                return;
            }

            var carrier = NetworkFirstPersonController.FindByClientId(CarrierClientId.Value);
            carrier?.ClearHeldItem(this);

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = true;
            ApplyPhysicsState();
            ApplyVisibilityState();

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
        }

        public void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = false;

            body.isKinematic = true;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            body.isKinematic = false;
            ClearDynamicVelocity();

            ApplyPhysicsState();
            ApplyVisibilityState();
        }

        private void OnCarrierChanged(ulong previousCarrier, ulong currentCarrier)
        {
            var previousPlayer = NetworkFirstPersonController.FindByClientId(previousCarrier);
            previousPlayer?.ClearHeldItem(this);

            UnsubscribeFromCarrierActiveSlot();

            var currentPlayer = NetworkFirstPersonController.FindByClientId(currentCarrier);
            currentPlayer?.SetHeldItem(this);

            if (currentPlayer != null)
            {
                subscribedCarrier = currentPlayer;
                currentPlayer.ActiveInventorySlot.OnValueChanged += OnCarrierActiveSlotChanged;
            }

            ApplyVisibilityState();
        }

        private void OnCarriedChanged(bool previousValue, bool currentValue)
        {
            ApplyPhysicsState();
            ApplyColliderState();
            ApplyVisibilityState();
        }

        private void OnDepositedChanged(bool previousValue, bool currentValue)
        {
            ApplyPhysicsState();
            ApplyVisibilityState();
            ApplyColliderState();
        }

        private void OnSlotIndexChanged(int previousValue, int currentValue)
        {
            ApplyVisibilityState();
        }

        private void OnCarrierActiveSlotChanged(int previousValue, int currentValue)
        {
            ApplyVisibilityState();
        }

        private void UnsubscribeFromCarrierActiveSlot()
        {
            if (subscribedCarrier == null) return;
            subscribedCarrier.ActiveInventorySlot.OnValueChanged -= OnCarrierActiveSlotChanged;
            subscribedCarrier = null;
        }

        // Snap stowed items to the carrier each frame on the server so they follow as the
        // carrier walks. The active item is positioned by the owner via MoveCarriedServerRpc.
        private void LateUpdate()
        {
            if (!IsServer) return;
            if (!IsCarried.Value || IsDeposited.Value) return;

            var carrier = NetworkFirstPersonController.FindByClientId(CarrierClientId.Value);
            if (carrier == null) return;
            if (SlotIndex.Value == carrier.ActiveInventorySlot.Value) return;

            var stowedPos = carrier.transform.position + carrier.transform.up * 0.6f;
            transform.position = stowedPos;
            if (body != null) body.position = stowedPos;
        }

        public bool IsActiveInCarriersHand
        {
            get
            {
                if (!IsCarried.Value || IsDeposited.Value) return false;
                var carrier = NetworkFirstPersonController.FindByClientId(CarrierClientId.Value);
                if (carrier == null) return false;
                return SlotIndex.Value == carrier.ActiveInventorySlot.Value;
            }
        }

        private void ApplyPhysicsState()
        {
            if (body == null)
            {
                return;
            }

            body.useGravity = false;
            if (!IsServer)
            {
                body.isKinematic = true;
                if (sphericalGravity != null)
                {
                    sphericalGravity.enabled = false;
                }

                return;
            }

            body.isKinematic = IsCarried.Value || IsDeposited.Value;
            if (sphericalGravity != null)
            {
                sphericalGravity.enabled = !body.isKinematic;
            }

            ClearDynamicVelocity();
        }

        private void ClearDynamicVelocity()
        {
            if (body == null || body.isKinematic)
            {
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void ApplyVisibilityState()
        {
            var visible = !IsDeposited.Value;
            if (visible && IsCarried.Value)
            {
                // Stowed inventory items (carried but not in the active slot) are hidden so
                // only the item in the player's "hand" is visible.
                var carrier = NetworkFirstPersonController.FindByClientId(CarrierClientId.Value);
                if (carrier != null && SlotIndex.Value != carrier.ActiveInventorySlot.Value)
                    visible = false;
            }

            foreach (var itemRenderer in renderers)
            {
                if (itemRenderer != null)
                {
                    itemRenderer.enabled = visible;
                }
            }
        }

        private void ApplyColliderState()
        {
            var shouldEnable = !IsCarried.Value && !IsDeposited.Value;
            foreach (var itemCollider in colliders)
            {
                if (itemCollider != null)
                {
                    itemCollider.enabled = shouldEnable;
                }
            }
        }
    }
}
