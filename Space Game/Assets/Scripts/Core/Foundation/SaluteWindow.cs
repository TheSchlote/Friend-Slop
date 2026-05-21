using System.Collections.Generic;

namespace FriendSlop.Ship
{
    // Pure-logic helper for SaluteCoordinator. The coordinator records every
    // station salute as (clientId, saluteTime). On each server tick it asks
    // this helper: "has every currently-occupied station saluted within the
    // last windowSeconds?". When the answer is yes, the coordinator fires
    // PerfectCrewClientRpc and clears the salute log to debounce.
    //
    // Pinned by SaluteWindowTests.
    public struct SaluteRecord
    {
        public ulong clientId;
        public float saluteTime;

        public SaluteRecord(ulong clientId, float saluteTime)
        {
            this.clientId = clientId;
            this.saluteTime = saluteTime;
        }
    }

    public static class SaluteWindow
    {
        // Returns true if every occupied client id has at least one salute
        // record whose saluteTime is within `windowSeconds` of `now`.
        // Empty occupied list -> true (vacuously satisfied; coordinator
        // shouldn't celebrate when nobody is at a station, so the caller is
        // responsible for the "at least one occupied station" guard).
        public static bool AllOccupiedSaluteWithinWindow(
            IReadOnlyList<ulong> occupiedClientIds,
            IReadOnlyList<SaluteRecord> recentSalutes,
            float now,
            float windowSeconds)
        {
            if (occupiedClientIds == null || occupiedClientIds.Count == 0)
                return true;

            if (windowSeconds <= 0f)
                return false;

            for (var i = 0; i < occupiedClientIds.Count; i++)
            {
                var occupant = occupiedClientIds[i];
                if (!HasRecentSalute(occupant, recentSalutes, now, windowSeconds))
                    return false;
            }

            return true;
        }

        private static bool HasRecentSalute(
            ulong clientId,
            IReadOnlyList<SaluteRecord> recentSalutes,
            float now,
            float windowSeconds)
        {
            if (recentSalutes == null) return false;

            // Take the latest salute for this client (records can repeat if
            // a player double-presses; the coordinator doesn't dedupe).
            var latest = float.NegativeInfinity;
            var found = false;
            for (var i = 0; i < recentSalutes.Count; i++)
            {
                var r = recentSalutes[i];
                if (r.clientId != clientId) continue;
                if (!found || r.saluteTime > latest)
                {
                    latest = r.saluteTime;
                    found = true;
                }
            }

            if (!found) return false;
            return (now - latest) <= windowSeconds;
        }
    }
}
