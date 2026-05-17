using FriendSlop.Interaction;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Server-authoritative door. Toggled via RPC; open state synced via NetworkVariable.
    public class InteriorDoor : NetworkBehaviour, IFriendSlopInteractable
    {
        [SerializeField] private Collider doorCollider;
        [SerializeField] private Transform doorPivot;
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float animSpeed = 4f;

        public bool CanInteract(NetworkFirstPersonController player) => true;
        public string GetPrompt(NetworkFirstPersonController player)
            => _isOpen.Value ? "E close door" : "E open door";

        public void Interact(NetworkFirstPersonController player) => ToggleRpc();

        private readonly NetworkVariable<bool> _isOpen =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float _currentAngle;
        private float _targetAngle;

        public override void OnNetworkSpawn()
        {
            _isOpen.OnValueChanged += OnOpenChanged;
            ApplyAngle(_isOpen.Value ? openAngle : 0f, instant: true);
        }

        public override void OnNetworkDespawn()
        {
            _isOpen.OnValueChanged -= OnOpenChanged;
        }

        private void Update()
        {
            if (Mathf.Approximately(_currentAngle, _targetAngle)) return;
            // Use unscaled time while the 3D blueprint editor is open — it sets
            // Time.timeScale = 0 to freeze monsters/loot/AI, but the player still
            // needs to be able to swing doors to walk between rooms during live
            // edit. Outside build mode this is identical to Time.deltaTime.
            float dt = FriendSlop.Interiors.Blocks.BlockBlueprint3DEditor.IsAnyActive
                ? Time.unscaledDeltaTime : Time.deltaTime;
            _currentAngle = Mathf.MoveTowards(_currentAngle, _targetAngle, animSpeed * openAngle * dt);
            if (doorPivot != null)
                doorPivot.localRotation = Quaternion.Euler(0, _currentAngle, 0);
        }

        [Rpc(SendTo.Server)]
        public void ToggleRpc()
        {
            _isOpen.Value = !_isOpen.Value;
        }

        private void OnOpenChanged(bool _, bool isOpen)
        {
            _targetAngle = isOpen ? openAngle : 0f;
            if (doorCollider != null) doorCollider.enabled = !isOpen;
        }

        private void ApplyAngle(float angle, bool instant)
        {
            _currentAngle = instant ? angle : _currentAngle;
            _targetAngle  = angle;
            if (doorPivot != null)
                doorPivot.localRotation = Quaternion.Euler(0, _currentAngle, 0);
            if (doorCollider != null) doorCollider.enabled = angle < 1f;
        }
    }
}
