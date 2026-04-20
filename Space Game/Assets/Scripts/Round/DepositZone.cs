using FriendSlop.Loot;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    [RequireComponent(typeof(Collider))]
    public class DepositZone : NetworkBehaviour
    {
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryDeposit(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryDeposit(other);
        }

        private void TryDeposit(Collider other)
        {
            if (!IsServer || RoundManager.Instance == null)
            {
                return;
            }

            var loot = other.GetComponentInParent<NetworkLootItem>();
            if (loot != null)
            {
                RoundManager.Instance.ServerDepositLoot(loot);
            }
        }
    }
}
