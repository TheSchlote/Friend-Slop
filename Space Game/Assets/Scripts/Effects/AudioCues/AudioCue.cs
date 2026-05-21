using UnityEngine;

namespace FriendSlop.Effects
{
    // Static facade for playing named gameplay audio cues. Callers don't know
    // (or care) whether the cue resolves to a real AudioClip authored in
    // AudioCueRegistry or to a procedural placeholder. Resolution order:
    //
    //   1. If a registry was explicitly set via SetRegistry (tests, or a
    //      future bootstrap component), consult it.
    //   2. Otherwise, attempt a one-time Resources.Load<AudioCueRegistry>
    //      lookup from the conventional path. Result (or null) is cached.
    //   3. If a registry resolves a clip for this cue, play that.
    //   4. Otherwise, play the procedural fallback from
    //      ProceduralAudioCueFactory.
    //
    // The convention path is "AudioCueRegistry" under any Resources/ folder.
    // To wire real audio: create an AudioCueRegistry asset, place it under
    // Assets/Resources/, then drop AudioClips into the inspector slots one
    // cue at a time. Slots left null fall through to procedural — so a
    // partial wiring is always safe.
    public static class AudioCue
    {
        private const string ConventionResourcePath = "AudioCueRegistry";

        private static AudioCueRegistry _registry;
        private static bool _registryWasInjected;
        private static bool _registryResourceLookupAttempted;

        // Tests + future bootstrap path. Pass null to clear.
        public static void SetRegistry(AudioCueRegistry registry)
        {
            _registry = registry;
            _registryWasInjected = registry != null;
            _registryResourceLookupAttempted = false;
        }

        // Drops cached state. Used by tests so Resources lookup re-runs on
        // demand.
        public static void ResetForTests()
        {
            _registry = null;
            _registryWasInjected = false;
            _registryResourceLookupAttempted = false;
            ProceduralAudioCueFactory.ClearCacheForTests();
        }

        public static void Play(AudioCueId id, Vector3 position, float volumeMultiplier = 1f)
        {
            if (id == AudioCueId.None) return;

            // No-op in EditMode (no scene running, no AudioListener). The
            // implementation calls AudioSource.PlayClipAtPoint which spawns a
            // temporary GameObject and schedules Destroy(...) — both illegal
            // outside Play mode. Tests cover resolution via ResolveClipForTests
            // instead of going through Play().
            if (!Application.isPlaying) return;

            var clip = ResolveClip(id, out var registryVolume, out var usedRegistry);
            if (clip == null) return;

            var baseVolume = usedRegistry ? registryVolume : ProceduralAudioCueFactory.DefaultVolume(id);
            var finalVolume = Mathf.Clamp01(baseVolume * Mathf.Clamp01(volumeMultiplier));
            if (finalVolume <= 0f) return;

            AudioSource.PlayClipAtPoint(clip, position, finalVolume);
        }

        // Pure lookup: same resolution as Play but without the AudioSource
        // dispatch. Exposed so tests can verify routing.
        public static AudioClip ResolveClipForTests(AudioCueId id, out float volume, out bool usedRegistry)
        {
            return ResolveClip(id, out volume, out usedRegistry);
        }

        private static AudioClip ResolveClip(AudioCueId id, out float volume, out bool usedRegistry)
        {
            usedRegistry = false;
            volume = 1f;
            if (id == AudioCueId.None) return null;

            var registry = EnsureRegistry();
            if (registry != null && registry.TryResolve(id, out var clip, out var clipVolume))
            {
                usedRegistry = true;
                volume = clipVolume;
                return clip;
            }

            volume = ProceduralAudioCueFactory.DefaultVolume(id);
            return ProceduralAudioCueFactory.GetOrCreate(id);
        }

        private static AudioCueRegistry EnsureRegistry()
        {
            if (_registry != null) return _registry;
            if (_registryWasInjected) return _registry; // explicit null injection
            if (_registryResourceLookupAttempted) return _registry;

            _registryResourceLookupAttempted = true;
            // Resources.Load returns null if the asset doesn't exist; that's
            // fine — we just fall back to procedural for every cue.
            _registry = Resources.Load<AudioCueRegistry>(ConventionResourcePath);
            return _registry;
        }
    }
}
