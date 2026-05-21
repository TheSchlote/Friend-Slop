using FriendSlop.Ship;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Pins BoomboxPlayback — the pure helpers behind BoomboxStationBehavior.
    //   - NextTrack: (current+1) mod trackCount, with trackCount<=0 returning
    //     NoTrack (-1).
    //   - ComputeLocalPlaybackTime: wraps elapsed via mod clipLength so late
    //     joiners drop into the same place everyone else is. Zero
    //     clipLength returns 0 (no audio to align).
    public class BoomboxPlaybackTests
    {
        // ── NextTrack ──────────────────────────────────────────────────

        [Test]
        public void NextTrack_ZeroCount_ReturnsNoTrack()
        {
            Assert.AreEqual(BoomboxPlayback.NoTrack, BoomboxPlayback.NextTrack(0, 0));
            Assert.AreEqual(BoomboxPlayback.NoTrack, BoomboxPlayback.NextTrack(BoomboxPlayback.NoTrack, 0));
        }

        [Test]
        public void NextTrack_NegativeCount_ReturnsNoTrack()
        {
            Assert.AreEqual(BoomboxPlayback.NoTrack, BoomboxPlayback.NextTrack(0, -3));
        }

        [Test]
        public void NextTrack_FromNoTrack_GoesToZero()
        {
            // Starting from "stopped", the next track is the first one.
            Assert.AreEqual(0, BoomboxPlayback.NextTrack(BoomboxPlayback.NoTrack, 3));
        }

        [Test]
        public void NextTrack_FromZero_GoesToOne()
        {
            Assert.AreEqual(1, BoomboxPlayback.NextTrack(0, 3));
        }

        [Test]
        public void NextTrack_FromLast_WrapsToZero()
        {
            Assert.AreEqual(0, BoomboxPlayback.NextTrack(2, 3));
        }

        [Test]
        public void NextTrack_SingleTrack_StaysAtZero()
        {
            // One track => next always 0.
            Assert.AreEqual(0, BoomboxPlayback.NextTrack(0, 1));
            Assert.AreEqual(0, BoomboxPlayback.NextTrack(BoomboxPlayback.NoTrack, 1));
        }

        // ── ComputeLocalPlaybackTime ───────────────────────────────────

        [Test]
        public void ComputeLocalPlaybackTime_ZeroClipLength_ReturnsZero()
        {
            Assert.AreEqual(0d, BoomboxPlayback.ComputeLocalPlaybackTime(0d, 5d, 0f));
        }

        [Test]
        public void ComputeLocalPlaybackTime_NegativeClipLength_ReturnsZero()
        {
            // Defensive: AudioClip.length should never be negative, but the
            // helper must not divide by zero or produce NaN.
            Assert.AreEqual(0d, BoomboxPlayback.ComputeLocalPlaybackTime(0d, 5d, -3f));
        }

        [Test]
        public void ComputeLocalPlaybackTime_BeforeStart_ClampsToZero()
        {
            // Current < start (e.g., late-joiner with desynced clock):
            // negative elapsed clamps to 0.
            Assert.AreEqual(0d, BoomboxPlayback.ComputeLocalPlaybackTime(10d, 5d, 4f));
        }

        [Test]
        public void ComputeLocalPlaybackTime_WithinFirstLoop_ReturnsElapsed()
        {
            // 5 seconds into a 10s clip = playback position 5.
            Assert.AreEqual(5d, BoomboxPlayback.ComputeLocalPlaybackTime(0d, 5d, 10f), 0.0001d);
        }

        [Test]
        public void ComputeLocalPlaybackTime_ExactlyOneLoop_Returns0()
        {
            // Elapsed == clipLength means "starts of next loop" -> mod = 0.
            Assert.AreEqual(0d, BoomboxPlayback.ComputeLocalPlaybackTime(0d, 10d, 10f), 0.0001d);
        }

        [Test]
        public void ComputeLocalPlaybackTime_PartwayThroughSecondLoop_ReturnsModulo()
        {
            // 15s elapsed on a 10s clip -> position 5.
            Assert.AreEqual(5d, BoomboxPlayback.ComputeLocalPlaybackTime(0d, 15d, 10f), 0.0001d);
        }

        [Test]
        public void ComputeLocalPlaybackTime_MultipleLoops_ReturnsModulo()
        {
            // 17s elapsed on a 4s clip -> 17 mod 4 = 1.
            Assert.AreEqual(1d, BoomboxPlayback.ComputeLocalPlaybackTime(0d, 17d, 4f), 0.0001d);
        }

        [Test]
        public void ComputeLocalPlaybackTime_NonZeroStart_SubtractsBeforeMod()
        {
            // server started at t=100, currentServerTime=107, clipLength=3
            // -> elapsed=7, 7 mod 3 = 1.
            Assert.AreEqual(1d, BoomboxPlayback.ComputeLocalPlaybackTime(100d, 107d, 3f), 0.0001d);
        }
    }
}
