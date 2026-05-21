namespace FriendSlop.Effects
{
    // Named gameplay audio cues. The id is the contract; the actual sound is
    // resolved at runtime through AudioCueRegistry (asset slot) with a
    // procedural fallback (ProceduralAudioCueFactory) if no clip is wired.
    //
    // Add new cues here. Don't rely on integer values for serialization beyond
    // a single editor session — Unity will round-trip enum names, but if you
    // renumber an existing value, in-editor AudioCueRegistry entries will
    // re-bind by index, not by name. Append, don't insert.
    public enum AudioCueId
    {
        None = 0,
        LootPickup = 1,
        MonsterDetect = 2,
        MeteorWarning = 3,
        LaunchIgnition = 4,
        DamageTaken = 5,
    }
}
