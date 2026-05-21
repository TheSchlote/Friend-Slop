using FriendSlop.Effects;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Pins the AudioCue static facade routing contract: callers say
    // "Play LootPickup at position X" and the facade routes through the
    // registry when one is set, otherwise to the procedural factory.
    //
    // These tests use ResolveClipForTests so they don't depend on a working
    // AudioSource.PlayClipAtPoint (which requires a scene + AudioListener).
    public class AudioCueTests
    {
        [SetUp]
        public void Reset()
        {
            // Each test starts with no registry set and a cold procedural cache.
            AudioCue.ResetForTests();
        }

        [TearDown]
        public void Cleanup()
        {
            AudioCue.ResetForTests();
        }

        [Test]
        public void Resolve_NoneCueReturnsNull()
        {
            var clip = AudioCue.ResolveClipForTests(AudioCueId.None, out _, out var usedRegistry);

            Assert.IsNull(clip);
            Assert.IsFalse(usedRegistry);
        }

        [Test]
        public void Resolve_NoRegistrySet_FallsThroughToProcedural()
        {
            // ResetForTests cleared the registry; the Resources.Load fallback
            // also returns null in EditMode because no asset is on disk.
            var clip = AudioCue.ResolveClipForTests(AudioCueId.LootPickup, out var volume, out var usedRegistry);

            Assert.IsFalse(usedRegistry, "Must report registry-bypass for telemetry/debug");
            Assert.IsNotNull(clip, "Procedural fallback must always provide a clip for authored cue ids");
            Assert.That(volume, Is.InRange(0f, 1f));
        }

        [Test]
        public void Resolve_RegistryWithClip_UsesRegistry()
        {
            var registryClip = AudioClip.Create("registry-pickup", 1, 1, 44100, false);
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = registryClip, volume = 0.4f },
            });
            AudioCue.SetRegistry(registry);

            var clip = AudioCue.ResolveClipForTests(AudioCueId.LootPickup, out var volume, out var usedRegistry);

            Assert.IsTrue(usedRegistry, "Registry hit must report usedRegistry=true");
            Assert.AreSame(registryClip, clip);
            Assert.AreEqual(0.4f, volume, 0.001f);
        }

        [Test]
        public void Resolve_RegistryMissingCue_FallsThroughToProcedural()
        {
            // Registry authors LootPickup only; MeteorWarning must fall
            // through to procedural. This is the "partial wiring" path —
            // designers can drop in clips one at a time without silencing
            // the rest of the cue table.
            var pickupClip = AudioClip.Create("pickup", 1, 1, 44100, false);
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = pickupClip, volume = 1f },
            });
            AudioCue.SetRegistry(registry);

            var clip = AudioCue.ResolveClipForTests(AudioCueId.MeteorWarning, out _, out var usedRegistry);

            Assert.IsFalse(usedRegistry);
            Assert.IsNotNull(clip, "Cue not in registry must fall through to procedural, not silently no-op");
        }

        [Test]
        public void Play_InEditMode_IsNoOp()
        {
            // EditMode tests cannot invoke AudioSource.PlayClipAtPoint —
            // it schedules a Destroy(temporary GameObject) which Unity
            // rejects outside Play mode. AudioCue.Play guards on
            // Application.isPlaying so callers (NetworkLootItem.OnCarrierChanged
            // and friends) stay safe when reached from EditMode harness tests.
            Assert.DoesNotThrow(() => AudioCue.Play(AudioCueId.LootPickup, Vector3.zero),
                "AudioCue.Play must be a no-op in EditMode — it's invoked transitively by EditMode tests reaching OnCarrierChanged etc.");
        }

        [Test]
        public void SetRegistry_Null_ClearsExplicitInjection()
        {
            // SetRegistry(null) is the explicit "no registry" path — distinct
            // from the un-attempted-Resources-Load path. The facade should
            // still resolve via procedural.
            var pickupClip = AudioClip.Create("pickup", 1, 1, 44100, false);
            var registry = AudioCueRegistry.CreateForTests(new[]
            {
                new AudioCueRegistry.Entry { id = AudioCueId.LootPickup, clip = pickupClip, volume = 1f },
            });
            AudioCue.SetRegistry(registry);

            AudioCue.SetRegistry(null);

            var clip = AudioCue.ResolveClipForTests(AudioCueId.LootPickup, out _, out var usedRegistry);

            Assert.IsFalse(usedRegistry,
                "SetRegistry(null) must clear the explicit injection so subsequent lookups don't keep hitting the old registry");
            Assert.IsNotNull(clip);
        }
    }
}
