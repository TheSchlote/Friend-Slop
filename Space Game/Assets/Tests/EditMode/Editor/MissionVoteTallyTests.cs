using FriendSlop.Ship;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Pins MissionVoteTally — the pure helpers behind MissionVoteBehavior.
    // The contract:
    //   - WinningCandidate returns null when nothing has been voted for.
    //   - Single highest-tally wins.
    //   - Ties resolve to the LOWEST candidate index (deterministic).
    //   - CycleVote walks NoVote -> 0 -> 1 -> ... -> N-1 -> NoVote.
    //   - candidateCount == 0 short-circuits to NoVote.
    public class MissionVoteTallyTests
    {
        // ── WinningCandidate ───────────────────────────────────────────

        [Test]
        public void WinningCandidate_NullList_ReturnsNull()
        {
            Assert.IsNull(MissionVoteTally.WinningCandidate(null));
        }

        [Test]
        public void WinningCandidate_EmptyList_ReturnsNull()
        {
            Assert.IsNull(MissionVoteTally.WinningCandidate(new int[0]));
        }

        [Test]
        public void WinningCandidate_AllZeroes_ReturnsNull()
        {
            Assert.IsNull(MissionVoteTally.WinningCandidate(new[] { 0, 0, 0 }));
        }

        [Test]
        public void WinningCandidate_SingleWinner_ReturnsThatIndex()
        {
            Assert.AreEqual(1, MissionVoteTally.WinningCandidate(new[] { 1, 3, 2 }));
        }

        [Test]
        public void WinningCandidate_TieResolvesToLowestIndex()
        {
            // 2 and 2 — candidate 0 wins.
            Assert.AreEqual(0, MissionVoteTally.WinningCandidate(new[] { 2, 2, 0 }));
        }

        [Test]
        public void WinningCandidate_TieThreeWay_ReturnsZero()
        {
            Assert.AreEqual(0, MissionVoteTally.WinningCandidate(new[] { 1, 1, 1 }));
        }

        [Test]
        public void WinningCandidate_NegativeValuesIgnored()
        {
            // Negative entries mean "subtract" — shouldn't happen via the
            // behavior, but the helper must not pick them as the winner.
            Assert.AreEqual(2, MissionVoteTally.WinningCandidate(new[] { -5, 0, 1 }));
        }

        [Test]
        public void WinningCandidate_SinglePositiveAtLastIndex_ReturnsIt()
        {
            Assert.AreEqual(2, MissionVoteTally.WinningCandidate(new[] { 0, 0, 1 }));
        }

        // ── CycleVote ──────────────────────────────────────────────────

        [Test]
        public void CycleVote_FromNoVote_GoesToZero()
        {
            Assert.AreEqual((byte)0, MissionVoteTally.CycleVote(MissionVoteTally.NoVote, 3));
        }

        [Test]
        public void CycleVote_FromZero_GoesToOne()
        {
            Assert.AreEqual((byte)1, MissionVoteTally.CycleVote(0, 3));
        }

        [Test]
        public void CycleVote_FromOne_GoesToTwo()
        {
            Assert.AreEqual((byte)2, MissionVoteTally.CycleVote(1, 3));
        }

        [Test]
        public void CycleVote_FromLastIndex_GoesToNoVote()
        {
            Assert.AreEqual(MissionVoteTally.NoVote, MissionVoteTally.CycleVote(2, 3));
        }

        [Test]
        public void CycleVote_CandidateCountZero_ReturnsNoVote()
        {
            Assert.AreEqual(MissionVoteTally.NoVote, MissionVoteTally.CycleVote(MissionVoteTally.NoVote, 0));
            Assert.AreEqual(MissionVoteTally.NoVote, MissionVoteTally.CycleVote(0, 0));
        }

        [Test]
        public void CycleVote_CandidateCountNegative_ReturnsNoVote()
        {
            Assert.AreEqual(MissionVoteTally.NoVote, MissionVoteTally.CycleVote(0, -1));
        }

        [Test]
        public void CycleVote_SingleCandidate_ToggleBehavior()
        {
            // With one candidate, the cycle is NoVote -> 0 -> NoVote.
            Assert.AreEqual((byte)0, MissionVoteTally.CycleVote(MissionVoteTally.NoVote, 1));
            Assert.AreEqual(MissionVoteTally.NoVote, MissionVoteTally.CycleVote(0, 1));
        }
    }
}
