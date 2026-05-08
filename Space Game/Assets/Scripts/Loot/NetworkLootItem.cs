using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Interaction;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public partial class NetworkLootItem : NetworkBehaviour, IFriendSlopInteractable
    {
        private const ulong NoCarrier = ulong.MaxValue;

        [SerializeField] private string itemName = "Weird Junk";
        [SerializeField] private int value = 50;
        [SerializeField] private float carrySpeedMultiplier = 0.85f;
        [SerializeField] private float carryDistance = 2.1f;
        [SerializeField] private ShipPartType shipPartType = ShipPartType.None;
        // How long the player has to hold F to deposit this item. 0 = instant tap.
        // Ship parts override this to 0 in the property below since the assembly is
        // the round objective and shouldn't be gated by a hold timer.
        [SerializeField, Min(0f)] private float depositHoldSeconds = 1f;

        private Rigidbody body;
        private SphericalRigidbodyGravity sphericalGravity;
        private Collider[] colliders;
        private Renderer[] renderers;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float _monsterHitCooldown;

        // Declaration order matters: NGO deserializes NetworkVariables in this order, and
        // OnCarrierChanged reads SlotIndex.Value when wiring the carrier's inventory cache.
        // Keeping SlotIndex above CarrierClientId guarantees the slot is current by the
        // time the carrier-change handler fires on remote clients.
        public NetworkVariable<bool> IsCarried = new(false);
        // -1 = not in any inventory. Set by the server when the item is picked up.
        public NetworkVariable<int> SlotIndex = new(-1);
        public NetworkVariable<ulong> CarrierClientId = new(NoCarrier);
        public NetworkVariable<bool> IsDeposited = new(false);

        private NetworkFirstPersonController _cachedCarrier;
        private NetworkFirstPersonController subscribedCarrier;

        public string ItemName => itemName;
        public int Value => value;
        public float CarrySpeedMultiplier => carrySpeedMultiplier;
        public float CarryDistance => carryDistance;
        public ShipPartType ShipPartType => shipPartType;
        public bool IsShipPart => shipPartType != ShipPartType.None;
        public float DepositHoldSeconds => IsShipPart ? 0f : Mathf.Max(0f, depositHoldSeconds);

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
            _cachedCarrier = null;
        }

        private void OnCarrierChanged(ulong previousCarrier, ulong currentCarrier)
        {
            var previousPlayer = previousCarrier != NoCarrier
                ? NetworkFirstPersonController.FindByClientId(previousCarrier)
                : null;
            previousPlayer?.ClearHeldItem(this);

            UnsubscribeFromCarrierActiveSlot();

            _cachedCarrier = currentCarrier != NoCarrier
                ? NetworkFirstPersonController.FindByClientId(currentCarrier)
                : null;
            _cachedCarrier?.SetHeldItem(this);

            if (_cachedCarrier != null)
            {
                subscribedCarrier = _cachedCarrier;
                _cachedCarrier.ActiveInventorySlot.OnValueChanged += OnCarrierActiveSlotChanged;
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

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || IsCarried.Value || IsDeposited.Value) return;

            TryWakeFromCollision(collision);

            if (Time.time < _monsterHitCooldown) return;
            if (body == null || body.linearVelocity.magnitude < 3f) return;

            var monster = collision.collider.GetComponentInParent<RoamingMonster>();
            if (monster == null) return;

            monster.ServerTakeDamage(20);
            _monsterHitCooldown = Time.time + 0.5f;
        }

        // Snap stowed items to the carrier each frame on the server so they follow as the
        // carrier walks. The active item is positioned by the owner via MoveCarriedServerRpc.
        private void LateUpdate()
        {
            if (!IsServer) return;
            ValidateServerDepositHold();
            if (!IsCarried.Value || IsDeposited.Value) return;

            if (!TryGetCachedCarrier(CarrierClientId.Value, out var carrier)) return;
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
                if (!TryGetCachedCarrier(CarrierClientId.Value, out var carrier)) return false;
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
                if (TryGetCachedCarrier(CarrierClientId.Value, out var carrier)
                    && SlotIndex.Value != carrier.ActiveInventorySlot.Value)
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

        private bool TryGetCachedCarrier(ulong clientId, out NetworkFirstPersonController carrier)
        {
            carrier = _cachedCarrier;
            if (carrier == null || carrier.OwnerClientId != clientId)
            {
                carrier = null;
                return false;
            }

            return true;
        }
    }
}
