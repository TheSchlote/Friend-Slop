using System.Reflection;
using FriendSlop.Loot;
using FriendSlop.Player;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class NetworkLootItemCarrierCacheTests
    {
        private GameObject playerObject;
        private GameObject itemObject;

        [TearDown]
        public void TearDown()
        {
            NetworkFirstPersonController.ActivePlayers.Clear();

            if (itemObject != null)
            {
                Object.DestroyImmediate(itemObject);
                itemObject = null;
            }

            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
                playerObject = null;
            }
        }

        [Test]
        public void OnCarrierChanged_CachesAndClearsCarrierReference()
        {
            const ulong clientId = 42UL;
            var player = CreatePlayer(clientId);
            var item = CreateLootItem();
            item.SlotIndex.Value = 0;

            InvokeCarrierChanged(item, ulong.MaxValue, clientId);

            Assert.AreSame(player, GetCachedCarrier(item));

            InvokeCarrierChanged(item, clientId, ulong.MaxValue);

            Assert.IsNull(GetCachedCarrier(item));
        }

        private NetworkFirstPersonController CreatePlayer(ulong clientId)
        {
            playerObject = new GameObject("Carrier");
            var player = playerObject.AddComponent<NetworkFirstPersonController>();
            typeof(NetworkFirstPersonController)
                .GetProperty("OwnerClientId")
                ?.SetValue(player, clientId);
            NetworkFirstPersonController.ActivePlayers.Add(player);
            return player;
        }

        private NetworkLootItem CreateLootItem()
        {
            itemObject = new GameObject("Loot");
            var item = itemObject.AddComponent<NetworkLootItem>();
            InvokeLifecycle(item, "Awake");
            return item;
        }

        private static void InvokeCarrierChanged(NetworkLootItem item, ulong previousCarrier, ulong currentCarrier)
        {
            typeof(NetworkLootItem)
                .GetMethod("OnCarrierChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(item, new object[] { previousCarrier, currentCarrier });
        }

        private static NetworkFirstPersonController GetCachedCarrier(NetworkLootItem item)
        {
            return (NetworkFirstPersonController)typeof(NetworkLootItem)
                .GetField("_cachedCarrier", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(item);
        }

        private static void InvokeLifecycle(MonoBehaviour component, string methodName)
        {
            component.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(component, null);
        }
    }
}
