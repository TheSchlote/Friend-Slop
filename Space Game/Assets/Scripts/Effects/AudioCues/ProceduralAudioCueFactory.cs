using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Effects
{
    // Synthesizes a short placeholder AudioClip for each cue when no real
    // AudioClip is assigned in AudioCueRegistry. Clips are generated lazily
    // on first request and cached per id for the rest of the process.
    //
    // This mirrors TeleporterAudio's "self-contained, no asset import
    // required" approach. Each cue has a distinct envelope/timbre so a
    // playtester can tell cues apart even without real audio.
    //
    // Pure utility — testable without Unity scene infrastructure. The clip
    // creation path needs UnityEngine.AudioClip but no MonoBehaviour or
    // scene.
    public static class ProceduralAudioCueFactory
    {
        private const int SampleRate = 44100;

        private static readonly Dictionary<AudioCueId, AudioClip> Cache = new();

        // Resets the in-memory clip cache. Tests call this between cases so
        // generation timing/cache behavior is deterministic. Production code
        // never needs to call this.
        public static void ClearCacheForTests()
        {
            Cache.Clear();
        }

        public static AudioClip GetOrCreate(AudioCueId id)
        {
            if (id == AudioCueId.None) return null;
            if (Cache.TryGetValue(id, out var cached) && cached != null) return cached;

            var clip = Generate(id);
            Cache[id] = clip;
            return clip;
        }

        // Returns the default volume the procedural cue should play at.
        // Different cues sit at different perceived loudness; this keeps a
        // bright pickup chirp from drowning out a deep launch roar.
        public static float DefaultVolume(AudioCueId id)
        {
            switch (id)
            {
                case AudioCueId.LootPickup:    return 0.7f;
                case AudioCueId.MonsterDetect: return 0.85f;
                case AudioCueId.MeteorWarning: return 0.9f;
                case AudioCueId.LaunchIgnition:return 0.95f;
                case AudioCueId.DamageTaken:   return 0.75f;
                default:                       return 0.7f;
            }
        }

        private static AudioClip Generate(AudioCueId id)
        {
            switch (id)
            {
                case AudioCueId.LootPickup:     return GenerateLootPickup();
                case AudioCueId.MonsterDetect:  return GenerateMonsterDetect();
                case AudioCueId.MeteorWarning:  return GenerateMeteorWarning();
                case AudioCueId.LaunchIgnition: return GenerateLaunchIgnition();
                case AudioCueId.DamageTaken:    return GenerateDamageTaken();
                default:                        return null;
            }
        }

        // --- Per-cue synthesizers ---------------------------------------

        // Short bright two-step chirp. Reads as "got it!"
        private static AudioClip GenerateLootPickup()
        {
            const float duration = 0.18f;
            var samples = AllocSamples(duration, out var n);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var progress = t / duration;
                // 900Hz to 1300Hz step at the midpoint — two-note "blip".
                var freq = progress < 0.5f ? 900f : 1300f;
                var attack = Mathf.Min(progress * 12f, 1f);
                var decay = Mathf.Exp(-progress * 7f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * attack * decay * 0.55f;
            }
            return Build("AudioCue_LootPickup", samples, n);
        }

        // Low menacing rumble + slow upward sweep. The "I see you" cue.
        private static AudioClip GenerateMonsterDetect()
        {
            const float duration = 0.65f;
            var samples = AllocSamples(duration, out var n);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var progress = t / duration;
                var sweep = Mathf.Lerp(80f, 200f, progress);
                // Add a quiet noise layer for "growl" texture.
                var noise = (Mathf.PerlinNoise(t * 30f, 0.5f) - 0.5f) * 0.4f;
                var attack = Mathf.Min(progress * 4f, 1f);
                var decay = Mathf.Exp(-progress * 1.8f);
                var tone = Mathf.Sin(2f * Mathf.PI * sweep * t);
                samples[i] = (tone * 0.6f + noise) * attack * decay * 0.75f;
            }
            return Build("AudioCue_MonsterDetect", samples, n);
        }

        // Descending whistle — gives players a moment to look up.
        private static AudioClip GenerateMeteorWarning()
        {
            const float duration = 0.85f;
            var samples = AllocSamples(duration, out var n);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var progress = t / duration;
                // 2200Hz down to 400Hz — classic falling-projectile arc.
                var sweep = Mathf.Lerp(2200f, 400f, progress);
                // Slight tremolo from a 6Hz LFO so it sounds like wind, not a sine.
                var lfo = 1f + Mathf.Sin(2f * Mathf.PI * 6f * t) * 0.08f;
                var attack = Mathf.Min(progress * 6f, 1f);
                var decay = Mathf.Exp(-progress * 0.6f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * sweep * lfo * t) * attack * decay * 0.55f;
            }
            return Build("AudioCue_MeteorWarning", samples, n);
        }

        // Deep ramp-up roar. The "we're going" cue.
        private static AudioClip GenerateLaunchIgnition()
        {
            const float duration = 1.1f;
            var samples = AllocSamples(duration, out var n);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var progress = t / duration;
                // 60Hz drone with a slow climb to 110Hz, layered noise for thrust.
                var fundamental = Mathf.Lerp(60f, 110f, progress);
                var tone = Mathf.Sin(2f * Mathf.PI * fundamental * t) * 0.55f;
                var noise = (Mathf.PerlinNoise(t * 80f, 1f) - 0.5f) * 0.7f;
                // Ramp-in over the first half second, then sustain.
                var attack = Mathf.Min(progress * 2.5f, 1f);
                samples[i] = (tone + noise) * attack * 0.7f;
            }
            return Build("AudioCue_LaunchIgnition", samples, n);
        }

        // Short thud + noise burst. The "ow" cue.
        private static AudioClip GenerateDamageTaken()
        {
            const float duration = 0.18f;
            var samples = AllocSamples(duration, out var n);
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)SampleRate;
                var progress = t / duration;
                // 180Hz thump + filtered noise.
                var thump = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.6f;
                var noise = (Mathf.PerlinNoise(t * 200f, 2f) - 0.5f) * 0.5f;
                var attack = Mathf.Min(progress * 25f, 1f);
                var decay = Mathf.Exp(-progress * 9f);
                samples[i] = (thump + noise) * attack * decay * 0.75f;
            }
            return Build("AudioCue_DamageTaken", samples, n);
        }

        // --- Helpers ----------------------------------------------------

        private static float[] AllocSamples(float durationSeconds, out int sampleCount)
        {
            sampleCount = Mathf.Max(1, Mathf.RoundToInt(SampleRate * durationSeconds));
            return new float[sampleCount];
        }

        private static AudioClip Build(string name, float[] samples, int sampleCount)
        {
            var clip = AudioClip.Create(name, sampleCount, channels: 1, frequency: SampleRate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
