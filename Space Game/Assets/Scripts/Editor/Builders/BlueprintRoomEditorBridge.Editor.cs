using UnityEditor;
using FriendSlop.Interiors;
using FriendSlop.Interiors.Blueprints;

namespace FriendSlop.Editor
{
    // Editor-side hook that wires the runtime bridge's delegate to the actual
    // builder implementation. Runs once on editor load and again on any domain
    // reload (script recompile, play-mode enter/exit) so the delegate is always
    // pointing at a valid method.
    [InitializeOnLoad]
    internal static class BlueprintRoomEditorBridgeEditor
    {
        static BlueprintRoomEditorBridgeEditor()
        {
            BlueprintRoomEditorBridge.RegeneratePrefabImpl = Regenerate;
        }

        [InitializeOnLoadMethod]
        private static void Init()
        {
            BlueprintRoomEditorBridge.RegeneratePrefabImpl = Regenerate;
        }

        private static void Regenerate(RoomDefinition def)
        {
            FriendSlopSceneBuilder.RegenerateRoomPrefabFromDefinition(def);
        }
    }
}
