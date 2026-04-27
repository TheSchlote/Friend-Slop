using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Loot
{
    [CreateAssetMenu(menuName = "Friend Slop/Loot Pool", fileName = "LootPool")]
    public class LootPool : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public NetworkLootItem prefab;
            public LootRarity rarity = LootRarity.Common;

            // 0 → use the rarity's DefaultRollWeight. Per-entry overrides let designers
            // bias a specific item without pulling the whole rarity tier with it.
            [Min(0f)] public float weightOverride;

            public float ResolvedWeight => weightOverride > 0f ? weightOverride : rarity.DefaultRollWeight();
        }

        [SerializeField] private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        public NetworkLootItem Roll() => Roll(out _);

        public NetworkLootItem Roll(out LootRarity rolledRarity)
        {
            rolledRarity = LootRarity.Common;
            if (entries == null || entries.Count == 0) return null;

            var totalWeight = 0f;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.prefab == null) continue;
                totalWeight += Mathf.Max(0f, entry.ResolvedWeight);
            }
            if (totalWeight <= 0f) return null;

            var roll = Random.value * totalWeight;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.prefab == null) continue;
                var weight = Mathf.Max(0f, entry.ResolvedWeight);
                if (weight <= 0f) continue;
                roll -= weight;
                if (roll <= 0f)
                {
                    rolledRarity = entry.rarity;
                    return entry.prefab;
                }
            }

            // Floating-point safety net: return the last valid entry.
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry != null && entry.prefab != null)
                {
                    rolledRarity = entry.rarity;
                    return entry.prefab;
                }
            }
            return null;
        }

        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries.Clear();
            if (newEntries != null) entries.AddRange(newEntries);
        }
    }
}
