using System;
using UnityEngine;

namespace FriendSlop.Effects
{
    // Data-driven audio cue mapping. One asset holds the full table of named
    // cues; each entry can leave its AudioClip slot null to fall through to
    // ProceduralAudioCueFactory.
    //
    // Drop-in upgrade path: as real audio arrives, drop AudioClips into the
    // inspector slots one cue at a time. Day-one workflow needs no clips at
    // all — the procedural fallback covers every cue id.
    [CreateAssetMenu(menuName = "Friend Slop/Audio/Cue Registry", fileName = "AudioCueRegistry")]
    public class AudioCueRegistry : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AudioCueId id;
            [Tooltip("Optional. If null, the procedural fallback is used for this cue.")]
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        // Pure lookup. Returns false if the id isn't authored or its clip slot
        // is null; caller is expected to fall back to procedural in that case.
        // Volume defaults to 1.0 when the entry's volume field is zero (the
        // implicit default for an un-touched serialized struct field), so a
        // fresh registry asset doesn't silently silence every cue.
        public bool TryResolve(AudioCueId id, out AudioClip clip, out float volume)
        {
            clip = null;
            volume = 1f;
            if (id == AudioCueId.None) return false;
            if (entries == null) return false;

            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].id != id) continue;
                if (entries[i].clip == null) return false;
                clip = entries[i].clip;
                volume = entries[i].volume > 0f ? Mathf.Clamp01(entries[i].volume) : 1f;
                return true;
            }
            return false;
        }

        // Test seam: construct an in-memory registry without touching disk.
        public static AudioCueRegistry CreateForTests(Entry[] entries)
        {
            var instance = CreateInstance<AudioCueRegistry>();
            instance.entries = entries ?? Array.Empty<Entry>();
            return instance;
        }
    }
}
