using System.Linq;
using System.Reflection;
using FriendSlop.Loot;
using NUnit.Framework;
using Unity.Netcode;

namespace FriendSlop.Tests.EditMode
{
    public class NetworkLootItemDeclarationOrderTests
    {
        [Test]
        public void NetworkLootItem_SlotIndex_DeclaredBefore_CarrierClientId()
        {
            var fields = typeof(NetworkLootItem)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(field => field.FieldType.IsGenericType
                    && field.FieldType.GetGenericTypeDefinition() == typeof(NetworkVariable<>))
                .OrderBy(field => field.MetadataToken)
                .ToList();

            var slotIndex = fields.FindIndex(field => field.Name == nameof(NetworkLootItem.SlotIndex));
            var carrierClientId = fields.FindIndex(field => field.Name == nameof(NetworkLootItem.CarrierClientId));

            Assert.GreaterOrEqual(slotIndex, 0, "SlotIndex must remain a NetworkVariable on NetworkLootItem.");
            Assert.GreaterOrEqual(carrierClientId, 0, "CarrierClientId must remain a NetworkVariable on NetworkLootItem.");
            Assert.Greater(carrierClientId, slotIndex,
                "SlotIndex must be declared before CarrierClientId. NGO deserializes NetworkVariables in declaration order; OnCarrierChanged reads SlotIndex.Value.");
        }
    }
}
