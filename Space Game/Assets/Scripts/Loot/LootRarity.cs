using UnityEngine;

namespace FriendSlop.Loot
{
    public enum LootRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4
    }

    public static class LootRarityExtensions
    {
        // Default roll weights — rarer entries are exponentially less likely. Pools may
        // override per-entry, so this is just the fallback when an entry leaves weight at 0.
        public static float DefaultRollWeight(this LootRarity rarity) => rarity switch
        {
            LootRarity.Common => 60f,
            LootRarity.Uncommon => 25f,
            LootRarity.Rare => 10f,
            LootRarity.Epic => 4f,
            LootRarity.Legendary => 1f,
            _ => 1f
        };

        // Tint used both for the default item material and for any future UI affordances.
        public static Color DisplayTint(this LootRarity rarity) => rarity switch
        {
            LootRarity.Common => new Color(0.78f, 0.78f, 0.78f),
            LootRarity.Uncommon => new Color(0.40f, 0.85f, 0.40f),
            LootRarity.Rare => new Color(0.32f, 0.58f, 0.98f),
            LootRarity.Epic => new Color(0.74f, 0.38f, 0.96f),
            LootRarity.Legendary => new Color(0.98f, 0.74f, 0.18f),
            _ => Color.white
        };

        public static string DisplayName(this LootRarity rarity) => rarity.ToString();
    }
}
