using System.Collections.Generic;

namespace FriendSlop.Ship
{
    // Pure-logic helpers for DartboardStationBehavior. The radius bands are
    // the contract — designers can re-tune them, but every change must move
    // a test. Pinned by DartScoringTests.
    public static class DartScoring
    {
        // Radius is normalized so 0 = bullseye, 1 = edge of the board, >1 =
        // miss. Bands are closed on the left and open on the right except for
        // the outer miss boundary, which is open on the right too (radius
        // exactly 1 still scores 1 — only strictly-greater misses).
        public static int ScoreForRadius(float radius01)
        {
            if (radius01 < 0f) radius01 = 0f;

            if (radius01 <= 0.08f) return 50;   // bullseye
            if (radius01 <= 0.2f)  return 25;
            if (radius01 <= 0.4f)  return 10;
            if (radius01 <= 0.65f) return 5;
            if (radius01 <= 1.0f)  return 1;
            return 0;                            // off the board
        }

        // Returns the highest score in the list, or 0 if the list is null or
        // empty. Used to drive the "current high score" line on the dart-
        // board prompt without bothering the test harness with a tuple type.
        public static int FindBest(IReadOnlyList<int> scores)
        {
            if (scores == null) return 0;

            var best = 0;
            for (var i = 0; i < scores.Count; i++)
            {
                var s = scores[i];
                if (s > best) best = s;
            }
            return best;
        }
    }
}
