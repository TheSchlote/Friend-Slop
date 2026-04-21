using FriendSlop.Loot;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    [RequireComponent(typeof(Collider))]
    public class LaunchpadZone : MonoBehaviour
    {
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            TrySubmit(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TrySubmit(other);
        }

        private void TrySubmit(Collider other)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || RoundManager.Instance == null)
                return;

            var loot = other.GetComponentInParent<NetworkLootItem>();
            if (loot != null)
            {
                RoundManager.Instance.ServerSubmitToLaunchpad(loot);
                return;
            }

            var player = other.GetComponentInParent<NetworkFirstPersonController>();
            if (player != null && player.HeldItem != null)
            {
                RoundManager.Instance.ServerSubmitToLaunchpad(player.HeldItem);
            }
        }
    }
}
