using System.Collections.Generic;
using FriendSlop.Loot;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    [RequireComponent(typeof(Collider))]
    public class DepositZone : NetworkBehaviour, IItemDepositSurface
    {
        // Tracks which players are currently standing in the trigger so PlayerInteractor
        // can offer the F-deposit prompt without re-querying physics overlap each frame.
        private readonly HashSet<ulong> playersInside = new();

        public string DepositLabel => "deposit";

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnEnable()
        {
            ItemDepositSurface.Register(this);
        }

        private void OnDisable()
        {
            ItemDepositSurface.Unregister(this);
            playersInside.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<NetworkFirstPersonController>();
            if (player != null) playersInside.Add(player.OwnerClientId);
        }

        private void OnTriggerExit(Collider other)
        {
            var player = other.GetComponentInParent<NetworkFirstPersonController>();
            if (player != null) playersInside.Remove(player.OwnerClientId);
        }

        public bool ContainsPlayer(NetworkFirstPersonController player)
        {
            return player != null && playersInside.Contains(player.OwnerClientId);
        }

        public bool Accepts(NetworkLootItem item)
        {
            // Junk loot only - ship parts have to go to the launchpad to satisfy the
            // assembly objective.
            return item != null && !item.IsShipPart;
        }

        public void ServerSubmit(NetworkLootItem item)
        {
            if (!IsServer || RoundManager.Instance == null) return;
            RoundManager.Instance.ServerDepositLoot(item);
        }
    }
}
