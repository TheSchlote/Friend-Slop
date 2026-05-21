using System.Collections.Generic;

namespace FriendSlop.Ship
{
    // Pure-logic helpers for the mission-vote station. Pinned by
    // MissionVoteTallyTests so the cycle / winning-candidate rules can be
    // reasoned about without a NetworkBehaviour harness.
    //
    // "No vote" is represented as byte.MaxValue throughout — it's the value
    // a NetworkList<MissionVoteEntry> never stores (we omit the entry
    // entirely), but the cycle helper needs a sentinel to round-trip through.
    public static class MissionVoteTally
    {
        public const byte NoVote = byte.MaxValue;

        // Returns the winning candidate index, or null if no votes were cast.
        // Deterministic tiebreak: the lowest candidate index wins so playtest
        // results don't depend on iteration order.
        public static int? WinningCandidate(IReadOnlyList<int> votesPerCandidate)
        {
            if (votesPerCandidate == null || votesPerCandidate.Count == 0)
                return null;

            var bestIndex = -1;
            var bestVotes = 0;
            for (var i = 0; i < votesPerCandidate.Count; i++)
            {
                var v = votesPerCandidate[i];
                if (v <= 0) continue;
                if (v > bestVotes)
                {
                    bestVotes = v;
                    bestIndex = i;
                }
                // Strictly-greater-than means earlier indices win ties.
            }

            return bestIndex < 0 ? (int?)null : bestIndex;
        }

        // Cycles a vote: NoVote -> 0 -> 1 -> ... -> candidateCount-1 -> NoVote.
        // candidateCount <= 0 short-circuits to NoVote so callers can hand off
        // the user's press to a no-op without a special case at the call site.
        public static byte CycleVote(byte currentVote, int candidateCount)
        {
            if (candidateCount <= 0) return NoVote;

            if (currentVote == NoVote)
                return 0;

            // Wrap to NoVote once we've passed the last real index.
            var next = currentVote + 1;
            if (next >= candidateCount)
                return NoVote;

            return (byte)next;
        }
    }
}
