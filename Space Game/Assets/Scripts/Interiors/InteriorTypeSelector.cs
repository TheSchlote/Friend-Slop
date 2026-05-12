using FriendSlop.Interaction;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Press E to cycle through every BuildingDefinition in the catalog. The InteriorEntrance
    // that points at this selector uses CurrentDefinition the next time someone enters.
    // Host-authoritative — clients can press E too; the RPC routes to the server.
    [RequireComponent(typeof(Collider))]
    public class InteriorTypeSelector : NetworkBehaviour, IFriendSlopInteractable
    {
        [SerializeField] private InteriorCatalog catalog;
        [SerializeField] private TextMesh label;

        private readonly NetworkVariable<int> _selectedIndex =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public BuildingDefinition CurrentDefinition
        {
            get
            {
                if (catalog == null || catalog.Buildings.Count == 0) return null;
                int idx = Mathf.Clamp(_selectedIndex.Value, 0, catalog.Buildings.Count - 1);
                return catalog.Buildings[idx];
            }
        }

        public override void OnNetworkSpawn()
        {
            _selectedIndex.OnValueChanged += OnIndexChanged;
            // First-time default — pick the Residential building if it's in the catalog.
            // The server sets the NetworkVariable; clients pick it up via OnValueChanged.
            if (IsServer && catalog != null && catalog.Buildings.Count > 0)
            {
                for (int i = 0; i < catalog.Buildings.Count; i++)
                {
                    var def = catalog.Buildings[i];
                    if (def == null) continue;
                    if (def.name != null && def.name.Contains("Residential"))
                    {
                        _selectedIndex.Value = i;
                        break;
                    }
                }
            }
            RefreshLabel();
        }

        public override void OnNetworkDespawn()
        {
            _selectedIndex.OnValueChanged -= OnIndexChanged;
        }

        public bool CanInteract(NetworkFirstPersonController player) => true;

        public string GetPrompt(NetworkFirstPersonController player)
        {
            var current = CurrentDefinition;
            return current != null
                ? $"E cycle type ({current.DisplayName})"
                : "E cycle type";
        }

        public void Interact(NetworkFirstPersonController player) => CycleRpc();

        [Rpc(SendTo.Server)]
        private void CycleRpc()
        {
            if (catalog == null || catalog.Buildings.Count == 0) return;
            _selectedIndex.Value = (_selectedIndex.Value + 1) % catalog.Buildings.Count;
        }

        private void OnIndexChanged(int _, int __) => RefreshLabel();

        private void RefreshLabel()
        {
            if (label == null) return;
            var current = CurrentDefinition;
            label.text = current != null
                ? $"Current: {current.DisplayName}"
                : "(no catalog assigned)";
        }
    }
}
