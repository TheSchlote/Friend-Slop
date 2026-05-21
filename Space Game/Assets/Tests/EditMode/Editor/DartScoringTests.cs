using FriendSlop.Ship;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Pins DartScoring.ScoreForRadius — the radius bands are the contract
    // and any rebalance has to move a test. Each band is checked at its
    // lower edge, mid-band, and just inside the upper boundary. Miss at
    // radius > 1.0.
    public class DartScoringTests
    {
        [Test]
        public void Bullseye_ExactZero_Returns50()
        {
            Assert.AreEqual(50, DartScoring.ScoreForRadius(0f));
        }

        [Test]
        public void Bullseye_MidBand_Returns50()
        {
            Assert.AreEqual(50, DartScoring.ScoreForRadius(0.04f));
        }

        [Test]
        public void Bullseye_UpperEdge_Returns50()
        {
            // 0.08 is inclusive.
            Assert.AreEqual(50, DartScoring.ScoreForRadius(0.08f));
        }

        [Test]
        public void InnerRing_JustAboveBullseye_Returns25()
        {
            Assert.AreEqual(25, DartScoring.ScoreForRadius(0.081f));
        }

        [Test]
        public void InnerRing_MidBand_Returns25()
        {
            Assert.AreEqual(25, DartScoring.ScoreForRadius(0.15f));
        }

        [Test]
        public void InnerRing_UpperEdge_Returns25()
        {
            Assert.AreEqual(25, DartScoring.ScoreForRadius(0.2f));
        }

        [Test]
        public void MiddleRing_MidBand_Returns10()
        {
            Assert.AreEqual(10, DartScoring.ScoreForRadius(0.3f));
        }

        [Test]
        public void MiddleRing_UpperEdge_Returns10()
        {
            Assert.AreEqual(10, DartScoring.ScoreForRadius(0.4f));
        }

        [Test]
        public void OuterRing_MidBand_Returns5()
        {
            Assert.AreEqual(5, DartScoring.ScoreForRadius(0.5f));
        }

        [Test]
        public void OuterRing_UpperEdge_Returns5()
        {
            Assert.AreEqual(5, DartScoring.ScoreForRadius(0.65f));
        }

        [Test]
        public void Edge_MidBand_Returns1()
        {
            Assert.AreEqual(1, DartScoring.ScoreForRadius(0.8f));
        }

        [Test]
        public void Edge_BoundaryRadiusOne_Returns1()
        {
            // Exactly 1.0 still scores 1 — only strictly > 1 misses.
            Assert.AreEqual(1, DartScoring.ScoreForRadius(1.0f));
        }

        [Test]
        public void Miss_JustOverOne_ReturnsZero()
        {
            Assert.AreEqual(0, DartScoring.ScoreForRadius(1.0001f));
        }

        [Test]
        public void Miss_Far_ReturnsZero()
        {
            Assert.AreEqual(0, DartScoring.ScoreForRadius(5f));
        }

        [Test]
        public void NegativeRadius_ClampsToBullseye()
        {
            // A negative radius is nonsense but shouldn't crash; clamping to
            // bullseye is the friendliest behavior.
            Assert.AreEqual(50, DartScoring.ScoreForRadius(-0.5f));
        }

        [Test]
        public void FindBest_NullList_ReturnsZero()
        {
            Assert.AreEqual(0, DartScoring.FindBest(null));
        }

        [Test]
        public void FindBest_EmptyList_ReturnsZero()
        {
            Assert.AreEqual(0, DartScoring.FindBest(new int[0]));
        }

        [Test]
        public void FindBest_PicksHighest()
        {
            Assert.AreEqual(50, DartScoring.FindBest(new[] { 5, 10, 25, 50, 25 }));
        }

        [Test]
        public void FindBest_AllNegative_ReturnsZero()
        {
            // FindBest starts at 0 and only replaces with strictly greater
            // entries — negative scores stay below the floor.
            Assert.AreEqual(0, DartScoring.FindBest(new[] { -5, -10 }));
        }
    }
}
