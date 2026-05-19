using System;

namespace FriendSlop.Interiors.Blueprints
{
    // Cross-asmdef bridge. The runtime BlueprintEditor (FriendSlop.Gameplay) needs to
    // ask the editor builder (FriendSlop.Editor) to regenerate a room's prefab after
    // the user edits its RoomDefinition. Asmdef isolation prevents the runtime asmdef
    // from referencing editor code directly, so we expose a static delegate that the
    // editor side fills in via [InitializeOnLoadMethod] at editor start.
    //
    // In standalone builds the delegate is never assigned and calls are no-ops, which
    // is the correct behaviour — editing room definitions is editor-only.
    public static class BlueprintRoomEditorBridge
    {
        public static Action<RoomDefinition> RegeneratePrefabImpl;

        public static void RegeneratePrefab(RoomDefinition def)
        {
            RegeneratePrefabImpl?.Invoke(def);
        }
    }
}
