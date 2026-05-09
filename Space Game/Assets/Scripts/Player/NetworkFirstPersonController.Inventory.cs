using FriendSlop.Loot;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    public partial class NetworkFirstPersonController
    {
        public const int InventorySize = 4;
        public NetworkVariable<int> ActiveInventorySlot = new(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        private readonly NetworkLootItem[] inventory = new NetworkLootItem[InventorySize];

        public NetworkLootItem HeldItem
        {
            get
            {
                var slot = Mathf.Clamp(ActiveInventorySlot.Value, 0, InventorySize - 1);
                return inventory[slot];
            }
        }

        public bool HasHeldItem => HeldItem != null && HeldItem.IsCarried.Value;

        public NetworkLootItem GetInventoryItem(int slot)
        {
            if (slot < 0 || slot >= InventorySize) return null;
            return inventory[slot];
        }

        public int InventoryCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < InventorySize; i++) if (inventory[i] != null) count++;
                return count;
            }
        }

        // Called from NetworkLootItem.OnCarrierChanged on every client when an item's
        // carrier becomes this player. SlotIndex on the item is authoritative.
        public void SetHeldItem(NetworkLootItem item)
        {
            if (item == null) return;
            var slot = item.SlotIndex.Value;
            if (slot < 0 || slot >= InventorySize)
            {
                // Backward-compat path for callers that do not go through pickup: drop into
                // the first empty slot.
                if (!TryGetFreeInventorySlot(out slot)) return;
            }
            inventory[slot] = item;
        }

        public void ClearHeldItem(NetworkLootItem item)
        {
            if (item == null) return;
            for (var i = 0; i < InventorySize; i++)
            {
                if (inventory[i] == item) inventory[i] = null;
            }
        }

        public bool TryGetFreeInventorySlot(out int slot)
        {
            for (var i = 0; i < InventorySize; i++)
            {
                if (inventory[i] == null) { slot = i; return true; }
            }
            slot = -1;
            return false;
        }

        public void ServerSetActiveSlot(int slot)
        {
            if (!IsServer) return;
            ActiveInventorySlot.Value = Mathf.Clamp(slot, 0, InventorySize - 1);
        }

        // After a drop/deposit clears the active slot, jump to the next slot that still has
        // an item so the player keeps holding something instead of staring at empty hands.
        public void ServerCycleToNonEmptySlotIfActiveCleared()
        {
            if (!IsServer) return;
            var current = Mathf.Clamp(ActiveInventorySlot.Value, 0, InventorySize - 1);
            if (inventory[current] != null) return;
            for (var step = 1; step <= InventorySize; step++)
            {
                var candidate = (current + step) % InventorySize;
                if (inventory[candidate] != null)
                {
                    ActiveInventorySlot.Value = candidate;
                    return;
                }
            }
        }

        public void ServerForceDropHeld(Vector3 impulse)
        {
            if (!IsServer) return;
            for (var i = 0; i < InventorySize; i++)
            {
                var item = inventory[i];
                if (item != null) item.ServerDrop(impulse);
            }
        }
    }
}
