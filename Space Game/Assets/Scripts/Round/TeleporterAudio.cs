using UnityEngine;

namespace FriendSlop.Round
{
    // Lazy-generates a short teleport chirp the first time PlayAt is called and caches
    // it on each client. The clip lives in memory only - no asset import required, which
    // keeps the prototype self-contained until someone wires real audio in. Replace
    // GenerateClip() with a SerializeField + AudioClip reference once the project has
    // proper sound design.
    public static class TeleporterAudio
    {
        private const int SampleRate = 44100;
        private const float DurationSeconds = 0.35f;

        private static AudioClip _clip;

        public static void PlayAt(Vector3 worldPosition, float volume = 0.7f)
        {
            var clip = GetOrCreateClip();
            if (clip == null) return;
            AudioSource.PlayClipAtPoint(clip, worldPosition, Mathf.Clamp01(volume));
        }

        private static AudioClip GetOrCreateClip()
        {
            if (_clip != null) return _clip;
            _clip = GenerateClip();
            return _clip;
        }

        private static AudioClip GenerateClip()
        {
            var sampleCount = Mathf.RoundToInt(SampleRate * DurationSeconds);
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var t = i / (float)SampleRate;
                var progress = t / DurationSeconds;

                // Frequency sweep: 1400Hz -> 400Hz with a gentle wobble so it doesn't
                // sound like a flat sine glide.
                var sweep = Mathf.Lerp(1400f, 400f, progress);
                var wobble = Mathf.Sin(t * 60f) * 35f;
                var freq = sweep + wobble;

                // Envelope: 30ms attack to 1.0, then exponential decay back toward zero.
                var attack = Mathf.Min(progress * 8f, 1f);
                var decay = Mathf.Exp(-progress * 4f);
                var envelope = attack * decay;

                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.6f;
            }

            var clip = AudioClip.Create("TeleporterChirp", sampleCount, channels: 1, frequency: SampleRate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
