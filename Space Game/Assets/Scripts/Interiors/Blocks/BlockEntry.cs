using System;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // One block placed in a BlockBlueprintAsset. Cell + Kind + (optional) Edge
    // for wall-class blocks. Tags/Label only meaningful on Floor tiles. Rotation
    // for directional blocks (stairs). StyleTag picks a variant pool at
    // materialise time (e.g. "smooth" → any Wall_01..Wall_07 with that style).
    [Serializable]
    public struct BlockEntry
    {
        // Cell coords: (x, floor, z). Y is the floor index, not metres.
        public Vector3Int Cell;
        public BlockKind  Kind;
        // For edge-mounted blocks (Wall/Door/Window), which edge of `Cell` does
        // this block sit on. Ignored for cell-occupying blocks.
        public SocketDirection Edge;
        // Floor tile only: freeform spawn tags ("bedroom", "kitchen,messy").
        // Drives furniture catalog matching at materialise time.
        public string[] Tags;
        // Floor tile only: a short human-readable label rendered above the tile
        // in editor mode. Empty string = label hidden.
        public string Label;
        // 0..3 quarter-turns CW around Y. Meaningful for stairs (which way they
        // ascend) and door visuals (which way they swing open).
        public int Rotation;
        // Optional style tag. Materialiser picks a prefab variant whose StyleTag
        // matches (or any if empty). For walls between two rooms with different
        // styles, the materialiser uses the room a player approaches from first
        // — handled in BlockBlueprintMaterializer.
        public string StyleTag;

        // ── Per-wall two-sided overrides (Wall / Door only) ────────────────
        // When OverrideFront/Back is true, that side ignores the adjacent
        // room's RoomStyle and uses the explicit prefab name + colour index
        // here instead. Set by the Paint scroll-mode. "Front" = the side
        // facing the wall's own Cell; "Back" = the side facing the cell across
        // the Edge. Empty prefab name = keep the room's variant, only override
        // colour. ColorIndex -1 = BlockColorPalette.None (no tint).
        public bool   OverrideFront;
        public string FrontPrefabName;
        public int    FrontColorIndex;
        public bool   OverrideBack;
        public string BackPrefabName;
        public int    BackColorIndex;
    }

    // Per-room appearance, keyed by the Label shared by a room's Floor tiles.
    // Stored on BlockBlueprintAsset.RoomStyles. Empty prefab name = random
    // (hash-seeded per room, the legacy behaviour). ColorIndex -1 = no tint.
    [Serializable]
    public struct RoomStyle
    {
        public string Label;
        public string WallPrefabName;
        public int    WallColorIndex;
        public string DoorPrefabName;
        public int    DoorColorIndex;
    }
}
