using UnityEngine;

namespace FriendSlop.Loot
{
    // Pure helper for the per-mission loot-value budget model (BACKLOG §7).
    // The spawner rolls until the cumulative item value reaches the guaranteed
    // minimum (Min × budget); individual rolls that would push past the ceiling
    // (Max × budget) are skipped. Kept as static helpers so the budget decision
    // is unit-testable without standing up a NetworkManager.
    public static class LootBudget
    {
        public const float MinFraction = 1.2f;
        public const float MaxFraction = 2.0f;
        public const int MaxRollsSafetyCap = 256;

        public static int MinValueFor(int budget) =>
            budget <= 0 ? 0 : Mathf.RoundToInt(budget * MinFraction);

        public static int MaxValueFor(int budget) =>
            budget <= 0 ? 0 : Mathf.RoundToInt(budget * MaxFraction);

        // True once the cumulative spawned value has reached the guaranteed
        // minimum; the spawner can stop rolling.
        public static bool BudgetReached(int currentTotal, int budget) =>
            budget > 0 && currentTotal >= MinValueFor(budget);

        // True when accepting an item of `candidateValue` would keep the running
        // total at or below the ceiling. Negative values are treated as zero.
        public static bool ShouldAccept(int candidateValue, int currentTotal, int budget)
        {
            if (budget <= 0) return false;
            var safeValue = Mathf.Max(0, candidateValue);
            return currentTotal + safeValue <= MaxValueFor(budget);
        }
    }
}
