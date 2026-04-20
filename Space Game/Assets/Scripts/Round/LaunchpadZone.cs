using FriendSlop.Loot;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    [RequireComponent(typeof(Collider))]
    public class LaunchpadZone : NetworkBehaviour
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
            if (!IsServer || RoundManager.Instance == null)
            {
                return;
            }

            var loot = other.GetComponentInParent<NetworkLootItem>();
            if (loot != null)
            {
                RoundManager.Instance.ServerSubmitToLaunchpad(loot);
            }
        }
    }
}
