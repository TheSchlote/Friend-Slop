using System.Collections.Generic;
using FriendSlop.Ship;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Pins SaluteWindow.AllOccupiedSaluteWithinWindow — the predicate behind
    // SaluteCoordinator's "Perfect Crew" check. Contract:
    //   - empty occupied list -> true (vacuously satisfied; the coordinator
    //     guards the "at least one occupied station" condition itself).
    //   - one occupied with a recent salute -> true.
    //   - one occupied with a stale salute -> false.
    //   - one occupied with no salute logged -> false.
    //   - two occupied, both recent -> true.
    //   - two occupied, one stale -> false.
    //   - windowSeconds <= 0 -> false (no positive window means no Perfect
    //     Crew can fire).
    public class SaluteWindowTests
    {
        private const float Now = 100f;
        private const float Window = 1.5f;

        private static IReadOnlyList<ulong> Occupants(params ulong[] ids) => ids;
        private static IReadOnlyList<SaluteRecord> Salutes(params SaluteRecord[] records) => records;

        [Test]
        public void EmptyOccupied_ReturnsTrue()
        {
            Assert.IsTrue(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(),
                Salutes(new SaluteRecord(1, Now)),
                Now, Window));
        }

        [Test]
        public void NullOccupied_ReturnsTrue()
        {
            Assert.IsTrue(SaluteWindow.AllOccupiedSaluteWithinWindow(
                null,
                Salutes(new SaluteRecord(1, Now)),
                Now, Window));
        }

        [Test]
        public void OneOccupied_RecentSalute_ReturnsTrue()
        {
            Assert.IsTrue(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(new SaluteRecord(7, Now - 0.5f)),
                Now, Window));
        }

        [Test]
        public void OneOccupied_SaluteAtExactWindowEdge_ReturnsTrue()
        {
            // 1.5s ago is on the boundary — inclusive.
            Assert.IsTrue(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(new SaluteRecord(7, Now - Window)),
                Now, Window));
        }

        [Test]
        public void OneOccupied_StaleSalute_ReturnsFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(new SaluteRecord(7, Now - 5f)),
                Now, Window));
        }

        [Test]
        public void OneOccupied_NoSaluteLogged_ReturnsFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(),
                Now, Window));
        }

        [Test]
        public void OneOccupied_NullSalutes_ReturnsFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                null,
                Now, Window));
        }

        [Test]
        public void OneOccupied_SaluteForDifferentClient_ReturnsFalse()
        {
            // A salute from someone else doesn't count for occupant 7.
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(new SaluteRecord(3, Now)),
                Now, Window));
        }

        [Test]
        public void TwoOccupied_BothRecent_ReturnsTrue()
        {
            Assert.IsTrue(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7, 9),
                Salutes(
                    new SaluteRecord(7, Now - 0.2f),
                    new SaluteRecord(9, Now - 1.0f)),
                Now, Window));
        }

        [Test]
        public void TwoOccupied_OneStale_ReturnsFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7, 9),
                Salutes(
                    new SaluteRecord(7, Now - 0.2f),
                    new SaluteRecord(9, Now - 5.0f)),
                Now, Window));
        }

        [Test]
        public void TwoOccupied_OneMissing_ReturnsFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7, 9),
                Salutes(new SaluteRecord(7, Now)),
                Now, Window));
        }

        [Test]
        public void RepeatedSalutes_LatestWins()
        {
            // Player double-pressed; older record is stale, latest is recent.
            // Should still resolve true because we take the latest per client.
            Assert.IsTrue(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(
                    new SaluteRecord(7, Now - 10f),
                    new SaluteRecord(7, Now - 0.5f)),
                Now, Window));
        }

        [Test]
        public void RepeatedSalutes_AllStale_ReturnsFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(
                    new SaluteRecord(7, Now - 10f),
                    new SaluteRecord(7, Now - 5f)),
                Now, Window));
        }

        [Test]
        public void ZeroWindow_AlwaysFalse()
        {
            // Even a perfectly-current salute fails if the window is zero.
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(new SaluteRecord(7, Now)),
                Now, 0f));
        }

        [Test]
        public void NegativeWindow_AlwaysFalse()
        {
            Assert.IsFalse(SaluteWindow.AllOccupiedSaluteWithinWindow(
                Occupants(7),
                Salutes(new SaluteRecord(7, Now)),
                Now, -1f));
        }
    }
}
