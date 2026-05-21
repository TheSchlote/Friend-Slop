namespace FriendSlop.Ship
{
    // Pure-logic helpers for BoomboxStationBehavior. Two responsibilities:
    //   1. track cycling — pure modular arithmetic, but the trackCount==0
    //      case has to report "no playback" so the call site doesn't divide
    //      by zero.
    //   2. server-time-synced playback offset — late-joining clients need to
    //      drop the AudioSource head where everyone else's is, derived from
    //      (NetworkManager.ServerTime - serverStartTime) mod clipLength.
    //
    // Pinned by BoomboxPlaybackTests.
    public static class BoomboxPlayback
    {
        public const int NoTrack = -1;

        // Advances the current track index. Returns NoTrack (-1) when there
        // are no tracks to play; otherwise (current + 1) % trackCount with
        // negative inputs clamped to 0.
        public static int NextTrack(int currentTrack, int trackCount)
        {
            if (trackCount <= 0) return NoTrack;

            var next = currentTrack < 0 ? 0 : currentTrack + 1;
            return next % trackCount;
        }

        // Maps a (serverStartTime, currentServerTime, clipLength) triple to
        // the AudioSource.time value that puts a late-joining client at the
        // same playback position as everyone else.
        //
        // Returns 0 if clipLength <= 0 (no audio to align). Negative elapsed
        // is clamped to 0 — that happens if a client's local clock somehow
        // ran ahead of the server's start timestamp, which would otherwise
        // produce a nonsensical negative time.
        public static double ComputeLocalPlaybackTime(
            double serverStartTime,
            double currentServerTime,
            float clipLength)
        {
            if (clipLength <= 0f) return 0d;

            var elapsed = currentServerTime - serverStartTime;
            if (elapsed < 0d) elapsed = 0d;

            // Modulo on doubles via integer division.
            var loops = (long)(elapsed / clipLength);
            return elapsed - (loops * (double)clipLength);
        }
    }
}
