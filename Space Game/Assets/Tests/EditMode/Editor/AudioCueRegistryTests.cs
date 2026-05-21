using FriendSlop.Effects;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Pins the AudioCueRegistry lookup contract. Resolution rules:
    //   - None cue id  → always false (sentinel)
    //   - Empty/null entries → false (caller falls back to procedural)
    //   - Authored entry with null AudioClip → false (per-cue partial wiring
    //     keeps unauthored slots on the procedural fallback)
    //   - Authored entry with clip → returns clip + volume (zero volume in
    //     the serialized struct defaults to 1.0 so a fresh registry doesn't
    //     silently silence every cue)
    public class AudioCueRegistryTests
    {
        [Test]
        public void TryResolve_NoneCueIdReturnsFalse()
        {
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = MakeClip("a"), volume = 1f },
            });

            var ok = registry.TryResolve(AudioCueId.None, out var clip, out var volume);

            Assert.IsFalse(ok);
            Assert.IsNull(clip);
        }

        [Test]
        public void TryResolve_EmptyRegistryReturnsFalse()
        {
            var registry = AudioCueRegistry.CreateForTests(new AudioCueRegistry.Entry[0]);

            var ok = registry.TryResolve(AudioCueId.LootPickup, out _, out _);

            Assert.IsFalse(ok, "Empty registry must fall through to procedural");
        }

        [Test]
        public void TryResolve_UnauthoredCueReturnsFalse()
        {
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = MakeClip("a"), volume = 1f },
            });

            var ok = registry.TryResolve(AudioCueId.MeteorWarning, out _, out _);

            Assert.IsFalse(ok, "Unauthored cue must fall through to procedural");
        }

        [Test]
        public void TryResolve_AuthoredCueWithNullClipReturnsFalse()
        {
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = null, volume = 1f },
            });

            var ok = registry.TryResolve(AudioCueId.LootPickup, out _, out _);

            Assert.IsFalse(ok,
                "Authored entry with null clip must fall through — that's how per-cue partial wiring works");
        }

        [Test]
        public void TryResolve_AuthoredCueWithClipReturnsClipAndVolume()
        {
            var pickupClip = MakeClip("pickup");
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = pickupClip, volume = 0.6f },
            });

            var ok = registry.TryResolve(AudioCueId.LootPickup, out var clip, out var volume);

            Assert.IsTrue(ok);
            Assert.AreSame(pickupClip, clip);
            Assert.AreEqual(0.6f, volume, 0.001f);
        }

        [Test]
        public void TryResolve_ZeroVolumeFallsBackToFullVolume()
        {
            // Default-constructed Entry has volume=0; we don't want that to
            // silently silence the cue.
            var pickupClip = MakeClip("pickup");
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = pickupClip, volume = 0f },
            });

            registry.TryResolve(AudioCueId.LootPickup, out _, out var volume);

            Assert.AreEqual(1f, volume, 0.001f,
                "Zero (default struct) volume must coerce to 1.0 — a fresh registry asset cannot ship silent cues");
        }

        [Test]
        public void TryResolve_FirstMatchingEntryWins()
        {
            // Duplicate cue ids in the array is an authoring mistake; the
            // contract is "first wins" so a future inspector view doesn't
            // surface non-deterministic behavior.
            var primary = MakeClip("primary");
            var shadow = MakeClip("shadow");
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = primary, volume = 1f },
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = shadow,  volume = 1f },
            });

            registry.TryResolve(AudioCueId.LootPickup, out var clip, out _);

            Assert.AreSame(primary, clip);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static AudioClip MakeClip(string name)
        {
            // Length is irrelevant for the lookup contract; we just need a
            // non-null asset reference.
            return AudioClip.Create(name, 1, 1, 44100, false);
        }
    }
}
