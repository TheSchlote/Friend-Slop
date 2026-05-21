using FriendSlop.Effects;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Pins the procedural audio fallback contract: every named cue must produce
    // a non-null, non-empty AudioClip so AudioCue.Play never silently no-ops
    // when no real audio is wired. Also pins per-cue cache behavior so the
    // factory doesn't allocate a fresh AudioClip every Play call.
    public class ProceduralAudioCueFactoryTests
    {
        [SetUp]
        public void ClearCache()
        {
            // Each test starts from a cold cache so we can observe first-vs-
            // second-call behavior deterministically.
            ProceduralAudioCueFactory.ClearCacheForTests();
        }

        [TestCase(AudioCueId.LootPickup)]
        [TestCase(AudioCueId.MonsterDetect)]
        [TestCase(AudioCueId.MeteorWarning)]
        [TestCase(AudioCueId.LaunchIgnition)]
        [TestCase(AudioCueId.DamageTaken)]
        public void GetOrCreate_ReturnsNonNullClipPerCue(AudioCueId id)
        {
            var clip = ProceduralAudioCueFactory.GetOrCreate(id);

            Assert.IsNotNull(clip, $"No procedural fallback wired for {id}");
            Assert.Greater(clip.samples, 0, "Procedural clip must contain samples");
        }

        [Test]
        public void GetOrCreate_NoneCueReturnsNull()
        {
            var clip = ProceduralAudioCueFactory.GetOrCreate(AudioCueId.None);

            Assert.IsNull(clip, "AudioCueId.None is sentinel — must never produce a clip");
        }

        [Test]
        public void GetOrCreate_CachesClipsByCueId()
        {
            var first = ProceduralAudioCueFactory.GetOrCreate(AudioCueId.LootPickup);
            var second = ProceduralAudioCueFactory.GetOrCreate(AudioCueId.LootPickup);

            Assert.AreSame(first, second,
                "Second GetOrCreate(LootPickup) must reuse the cached clip, not synthesize again");
        }

        [Test]
        public void GetOrCreate_DifferentCuesReturnDifferentClips()
        {
            var pickup = ProceduralAudioCueFactory.GetOrCreate(AudioCueId.LootPickup);
            var damage = ProceduralAudioCueFactory.GetOrCreate(AudioCueId.DamageTaken);

            Assert.AreNotSame(pickup, damage,
                "Each cue id needs its own distinct synthesized clip");
        }

        [TestCase(AudioCueId.LootPickup)]
        [TestCase(AudioCueId.MonsterDetect)]
        [TestCase(AudioCueId.MeteorWarning)]
        [TestCase(AudioCueId.LaunchIgnition)]
        [TestCase(AudioCueId.DamageTaken)]
        public void DefaultVolume_StaysInUnitRange(AudioCueId id)
        {
            var volume = ProceduralAudioCueFactory.DefaultVolume(id);

            Assert.That(volume, Is.InRange(0f, 1f),
                $"DefaultVolume({id}) must be a normalized 0..1 multiplier so AudioSource.PlayClipAtPoint never clips");
        }
    }
}
