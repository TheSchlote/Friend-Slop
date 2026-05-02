using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Interaction;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public partial class NetworkLootItem : NetworkBehaviour, IFriendSlopInteractable
    {
        private const ulong NoCarrier = ulong.MaxValue;
        private const float ServerPickupMaxDistance = 4.25f;
        private const float ServerDepositHoldGraceSeconds = 0.1f;

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
        private ulong _depositHoldClientId = NoCarrier;
        private float _depositHoldReadyAt;
        private IItemDepositSurface _depositHoldSurface;

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
            if (!CanServerReachForPickup(player))
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

        private bool CanServerReachForPickup(NetworkFirstPersonController player)
        {
            var origin = player.PlayerCamera != null
                ? player.PlayerCamera.transform.position
                : player.transform.position + player.transform.up * 1.1f;
            var target = GetServerPickupTargetPoint();
            if ((target - origin).sqrMagnitude > ServerPickupMaxDistance * ServerPickupMaxDistance)
                return false;

            return HasClearPickupLine(player.transform.root, origin, transform.root, target);
        }

        private Vector3 GetServerPickupTargetPoint()
        {
            var hasBounds = false;
            var bounds = default(Bounds);
            if (colliders != null)
            {
                for (var i = 0; i < colliders.Length; i++)
                {
                    var itemCollider = colliders[i];
                    if (itemCollider == null || !itemCollider.enabled) continue;
                    if (!hasBounds)
                    {
                        bounds = itemCollider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(itemCollider.bounds);
                    }
                }
            }

            return hasBounds ? bounds.center : transform.position;
        }

        private static bool HasClearPickupLine(Transform playerRoot, Vector3 origin, Transform itemRoot, Vector3 target)
        {
            var offset = target - origin;
            var distance = offset.magnitude;
            if (distance <= 0.01f) return true;

            var hits = Physics.RaycastAll(origin, offset / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var hitTransform = hits[i].collider != null ? hits[i].collider.transform : null;
                if (hitTransform == null) continue;
                var hitRoot = hitTransform.root;
                if (hitRoot == playerRoot || hitRoot == itemRoot) continue;
                return false;
            }

            return true;
        }

        protected static Vector3 ResolveServerAimDirection(
            NetworkFirstPersonController player,
            Vector3 requestedDirection,
            float maxAngleDegrees)
        {
            if (player == null) return Vector3.forward;

            var serverDirection = player.GetServerViewDirection();
            if (requestedDirection.sqrMagnitude < 0.001f)
                return serverDirection;

            var normalizedRequest = requestedDirection.normalized;
            return Vector3.Angle(serverDirection, normalizedRequest) <= maxAngleDegrees
                ? normalizedRequest
                : serverDirection;
        }

        // Max legitimate throw impulse = throwImpulse(8) * chargeThrowMultiplier(3.5) ≈ 28.
        // Clamp at 30 to reject obviously spoofed magnitudes while allowing real throws.
        private const float MaxDropImpulseMagnitude = 30f;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDropServerRpc(Vector3 impulse, RpcParams rpcParams = default)
        {
            if (!IsHeldBy(rpcParams.Receive.SenderClientId))
                return;

            var clampedImpulse = impulse.sqrMagnitude > MaxDropImpulseMagnitude * MaxDropImpulseMagnitude
                ? impulse.normalized * MaxDropImpulseMagnitude
                : impulse;
            ServerDrop(clampedImpulse);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void BeginDepositHoldServerRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!TryResolveDepositSurface(senderId, out var surface))
            {
                ClearServerDepositHoldFor(senderId);
                return;
            }

            var holdSeconds = DepositHoldSeconds;
            if (holdSeconds <= 0f)
            {
                ClearServerDepositHoldFor(senderId);
                return;
            }

            _depositHoldClientId = senderId;
            _depositHoldSurface = surface;
            _depositHoldReadyAt = Time.time + Mathf.Max(0f, holdSeconds - ServerDepositHoldGraceSeconds);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void CancelDepositHoldServerRpc(RpcParams rpcParams = default)
        {
            ClearServerDepositHoldFor(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDepositServerRpc(RpcParams rpcParams = default)
        {
            var senderId = rpcParams.Receive.SenderClientId;
            if (!TryResolveDepositSurface(senderId, out var surface))
            {
                ClearServerDepositHoldFor(senderId);
                return;
            }

            if (DepositHoldSeconds > 0f)
            {
                if (_depositHoldClientId != senderId) return;
                if (_depositHoldSurface == null || !ReferenceEquals(_depositHoldSurface, surface)) return;
                if (Time.time < _depositHoldReadyAt) return;
            }

            ClearServerDepositHoldFor(senderId);
            surface.ServerSubmit(this);
        }

        private bool TryResolveDepositSurface(ulong senderId, out IItemDepositSurface surface)
        {
            surface = null;
            if (!IsHeldBy(senderId) || IsDeposited.Value) return false;

            var round = RoundManager.Instance;
            if (round == null || round.Phase.Value != RoundPhase.Active) return false;

            if (!TryGetCachedCarrier(senderId, out var carrier)) return false;

            surface = ItemDepositSurface.FindFor(carrier, this);
            return surface != null;
        }

        private void ClearServerDepositHold()
        {
            _depositHoldClientId = NoCarrier;
            _depositHoldReadyAt = 0f;
            _depositHoldSurface = null;
        }

        private void ClearServerDepositHoldFor(ulong clientId)
        {
            if (_depositHoldClientId != clientId) return;
            ClearServerDepositHold();
        }

        private void ValidateServerDepositHold()
        {
            if (_depositHoldClientId == NoCarrier) return;
            if (!TryResolveDepositSurface(_depositHoldClientId, out var surface)
                || _depositHoldSurface == null
                || !ReferenceEquals(_depositHoldSurface, surface))
            {
                ClearServerDepositHold();
            }
        }

        public void ServerDrop(Vector3 impulse)
        {
            if (!IsServer || !IsCarried.Value)
            {
                return;
            }

            TryGetCachedCarrier(CarrierClientId.Value, out var carrier);
            carrier?.ClearHeldItem(this);
            ClearServerDepositHold();

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

            TryGetCachedCarrier(CarrierClientId.Value, out var carrier);
            carrier?.ClearHeldItem(this);
            ClearServerDepositHold();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = true;
            ApplyPhysicsState();
            ApplyVisibilityState();

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
        }

        public void ServerDespawnForPlanetTravel()
        {
            if (!IsServer)
            {
                return;
            }

            var networkObject = NetworkObject;
            if (networkObject == null || !networkObject.IsSpawned)
            {
                Destroy(gameObject);
                return;
            }

            TryGetCachedCarrier(CarrierClientId.Value, out var carrier);
            carrier?.ClearHeldItem(this);
            ClearServerDepositHold();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = false;

            carrier?.ServerCycleToNonEmptySlotIfActiveCleared();
            networkObject.Despawn(destroy: true);
        }

        public virtual void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            // Ship-resting items keep their dropped position across round restarts so the
            // crew can build a hoard. Carried items and deposited items always reset back
            // to their planet spawn so the next round is winnable.
            var preservePosition = ShouldPreserveShipPosition();

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
            SlotIndex.Value = -1;
            IsDeposited.Value = false;
            ClearServerDepositHold();

            body.isKinematic = true;
            if (!preservePosition)
            {
                transform.SetPositionAndRotation(spawnPosition, spawnRotation);
                body.position = spawnPosition;
                body.rotation = spawnRotation;
            }
            body.isKinematic = false;
            ClearDynamicVelocity();

            ApplyPhysicsState();
            ApplyVisibilityState();
        }

        // True when the item is sitting (uncarried, undeposited) inside a flat-gravity
        // volume - i.e. on the ship deck. Planet loot uses sphere gravity, so it never
        // matches and always teleports back to its spawn anchor.
        private bool ShouldPreserveShipPosition()
        {
            if (IsCarried.Value || IsDeposited.Value) return false;
            return FlatGravityVolume.TryGetContaining(transform.position, out _);
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
