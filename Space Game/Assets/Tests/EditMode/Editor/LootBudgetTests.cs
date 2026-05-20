using FriendSlop.Loot;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Pins the per-mission loot-budget contract (BACKLOG §7):
    //   - Minimum = 1.2× budget (rolling stops once reached).
    //   - Maximum = 2.0× budget (individual rolls beyond ceiling are skipped).
    //   - Budget == 0 is the legacy/disabled path (every accept fails, every
    //     reach is false) so the spawner falls back to the per-point loop.
    public class LootBudgetTests
    {
        [Test]
        public void MinValueFor_AppliesFractionAndRoundsToNearestInt()
        {
            Assert.AreEqual(120, LootBudget.MinValueFor(100));
            Assert.AreEqual(600, LootBudget.MinValueFor(500));
        }

        [Test]
        public void MaxValueFor_AppliesFractionAndRoundsToNearestInt()
        {
            Assert.AreEqual(200, LootBudget.MaxValueFor(100));
            Assert.AreEqual(1000, LootBudget.MaxValueFor(500));
        }

        [Test]
        public void MinAndMaxValueFor_ReturnZeroForZeroOrNegativeBudget()
        {
            Assert.AreEqual(0, LootBudget.MinValueFor(0));
            Assert.AreEqual(0, LootBudget.MaxValueFor(0));
            Assert.AreEqual(0, LootBudget.MinValueFor(-100));
            Assert.AreEqual(0, LootBudget.MaxValueFor(-100));
        }

        [Test]
        public void BudgetReached_FalseBelowMin()
        {
            Assert.IsFalse(LootBudget.BudgetReached(0, 500));
            Assert.IsFalse(LootBudget.BudgetReached(100, 500));
            Assert.IsFalse(LootBudget.BudgetReached(599, 500), "599 < 600 = 1.2 × 500");
        }

        [Test]
        public void BudgetReached_TrueAtAndAboveMin()
        {
            Assert.IsTrue(LootBudget.BudgetReached(600, 500), "600 = 1.2 × 500");
            Assert.IsTrue(LootBudget.BudgetReached(800, 500));
            Assert.IsTrue(LootBudget.BudgetReached(1000, 500), "ceiling counts as reached");
        }

        [Test]
        public void BudgetReached_FalseForZeroOrNegativeBudget()
        {
            // Legacy path is selected by budget==0; the helper must not claim "reached".
            Assert.IsFalse(LootBudget.BudgetReached(0, 0));
            Assert.IsFalse(LootBudget.BudgetReached(9999, 0));
            Assert.IsFalse(LootBudget.BudgetReached(0, -100));
        }

        [Test]
        public void ShouldAccept_TrueWhenSumStaysAtOrUnderCeiling()
        {
            // Ceiling = 1000.
            Assert.IsTrue(LootBudget.ShouldAccept(50, 0, 500));
            Assert.IsTrue(LootBudget.ShouldAccept(200, 700, 500), "700 + 200 = 900 ≤ 1000");
            Assert.IsTrue(LootBudget.ShouldAccept(100, 900, 500), "exact ceiling is accepted");
        }

        [Test]
        public void ShouldAccept_FalseWhenSumWouldExceedCeiling()
        {
            // Ceiling = 1000.
            Assert.IsFalse(LootBudget.ShouldAccept(200, 900, 500), "900 + 200 > 1000");
            Assert.IsFalse(LootBudget.ShouldAccept(5000, 0, 500), "single huge roll over ceiling");
        }

        [Test]
        public void ShouldAccept_TreatsNegativeCandidateAsZero()
        {
            Assert.IsTrue(LootBudget.ShouldAccept(-50, 0, 500));
            Assert.IsTrue(LootBudget.ShouldAccept(-50, 1000, 500),
                "exact ceiling + negative-clamped-to-zero = still at ceiling, accepted");
        }

        [Test]
        public void ShouldAccept_FalseForZeroOrNegativeBudget()
        {
            // Legacy path: nothing should be accepted via the budget loop.
            Assert.IsFalse(LootBudget.ShouldAccept(50, 0, 0));
            Assert.IsFalse(LootBudget.ShouldAccept(50, 0, -100));
        }
    }
}
