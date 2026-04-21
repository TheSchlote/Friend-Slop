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

        public NetworkVariable<bool> IsCarried = new(false);
        public NetworkVariable<ulong> CarrierClientId = new(NoCarrier);
        public NetworkVariable<bool> IsDeposited = new(false);

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

        public string GetPrompt(NetworkFirstPersonController player)
        {
            if (IsHeldBy(player.OwnerClientId))
            {
                return $"Carrying {itemName}: Q drop | Right Mouse throw";
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

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPickupServerRpc(RpcParams rpcParams = default)
        {
            if (IsDeposited.Value || IsCarried.Value)
            {
                return;
            }

            var senderId = rpcParams.Receive.SenderClientId;
            var player = NetworkFirstPersonController.FindByClientId(senderId);
            if (player == null || player.HeldItem != null)
            {
                return;
            }

            IsCarried.Value = true;
            CarrierClientId.Value = senderId;
            ApplyPhysicsState();
            player.SetHeldItem(this);
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
            ApplyPhysicsState();

            body.AddForce(impulse, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * impulse.magnitude * 0.25f, ForceMode.VelocityChange);
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
            IsDeposited.Value = true;
            ApplyPhysicsState();
            ApplyVisibilityState();
        }

        public void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            IsCarried.Value = false;
            CarrierClientId.Value = NoCarrier;
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

            var currentPlayer = NetworkFirstPersonController.FindByClientId(currentCarrier);
            currentPlayer?.SetHeldItem(this);
        }

        private void OnCarriedChanged(bool previousValue, bool currentValue)
        {
            ApplyPhysicsState();
            ApplyColliderState();
        }

        private void OnDepositedChanged(bool previousValue, bool currentValue)
        {
            ApplyPhysicsState();
            ApplyVisibilityState();
            ApplyColliderState();
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
            foreach (var itemRenderer in renderers)
            {
                if (itemRenderer != null)
                {
                    itemRenderer.enabled = !IsDeposited.Value;
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
