using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Residential block blueprint: a flat list of BlockEntry on a grid. Replaces
    // the room-based BlueprintAsset for residential buildings — every wall,
    // floor, door, and window is placed explicitly by a designer in the F3
    // editor. Procedural generation doesn't touch this — it's pure authored data.
    //
    // CellMetres / WallHeightMetres are stored on the asset itself so the editor
    // and the materialiser stay in sync without referring back to a separate
    // BuildingDefinition. Defaults match the low-poly Synty wall and floor
    // assets in Assets/LowPolyInterior/.
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Block Blueprint", fileName = "BlockBlueprint")]
    public class BlockBlueprintAsset : ScriptableObject
    {
        [Tooltip("Designer-facing name shown in editor lists.")]
        public string DisplayName;

        [Tooltip("Edge length of one grid cell in metres. Set to match the width " +
                 "of the floor and wall assets you're building with. 2 m matches " +
                 "the LowPolyInterior pack defaults.")]
        public float CellMetres = 2f;

        [Tooltip("Height of one floor in metres. Wall assets should match. 4 m " +
                 "matches the existing FloorHeightMeters on BuildingDefinition.")]
        public float WallHeightMetres = 4f;

        [Tooltip("Seed for variant selection. All tiles in a room (same Label) " +
                 "share a variant computed from this seed + room index, so re-" +
                 "rolling the seed swaps every room's look at once.")]
        public int VariantSeed;

        public List<BlockEntry> Blocks = new();

        [Tooltip("Per-room wall + door appearance, keyed by Floor-tile Label. " +
                 "Two-sided walls read the room on each side and apply its " +
                 "RoomStyle; per-wall Paint overrides win.")]
        public List<RoomStyle> RoomStyles = new();

        // Look up (or default) the RoomStyle for a label. Returns a blank style
        // (random variant, no tint) when none is authored.
        public RoomStyle GetRoomStyle(string label)
        {
            if (!string.IsNullOrEmpty(label))
                for (int i = 0; i < RoomStyles.Count; i++)
                    if (RoomStyles[i].Label == label) return RoomStyles[i];
            return new RoomStyle { Label = label, WallColorIndex = -1, DoorColorIndex = -1 };
        }

        // Insert-or-update the RoomStyle for its Label.
        public void SetRoomStyle(RoomStyle style)
        {
            for (int i = 0; i < RoomStyles.Count; i++)
                if (RoomStyles[i].Label == style.Label) { RoomStyles[i] = style; return; }
            RoomStyles.Add(style);
        }
    }
}
