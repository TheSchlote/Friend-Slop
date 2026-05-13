using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors.Blueprints
{
    // Authoring-time, designer-edited interior layout. Replaces the procedural
    // generator's output for buildings that ship blueprints (residential).
    // Phase 1 holds the core layout data: which rooms sit where + per-edge wall
    // overrides. Later phases extend this with per-slot furniture overrides and
    // room variants (held on RoomDefinition itself, not here).
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Blueprint", fileName = "Blueprint")]
    public class BlueprintAsset : ScriptableObject
    {
        [Tooltip("Human-readable name shown in the editor's load list. Distinct from " +
                 "the asset's filename so blueprints can be renamed without losing refs.")]
        public string DisplayName;
        public List<RoomPlacement> Rooms = new();
        public List<EdgeOverride> EdgeOverrides = new();

        // Look up the override for the edge between two cells. Order-independent —
        // (A,B) and (B,A) return the same result. Returns Default when no override.
        public EdgeState GetEdgeState(Vector3Int cellA, Vector3Int cellB)
        {
            NormalizePair(ref cellA, ref cellB);
            for (int i = 0; i < EdgeOverrides.Count; i++)
                if (EdgeOverrides[i].CellA == cellA && EdgeOverrides[i].CellB == cellB)
                    return EdgeOverrides[i].State;
            return EdgeState.Default;
        }

        public static void NormalizePair(ref Vector3Int a, ref Vector3Int b)
        {
            if (Compare(a, b) > 0) { var t = a; a = b; b = t; }
        }
        private static int Compare(Vector3Int a, Vector3Int b)
        {
            if (a.y != b.y) return a.y - b.y;
            if (a.x != b.x) return a.x - b.x;
            return a.z - b.z;
        }
    }

    [Serializable]
    public struct RoomPlacement
    {
        public RoomDefinition Definition;
        // Grid pivot — same convention as PlacedRoom.GridPosition (cell coords).
        public Vector3Int GridPosition;
        // 0..3 quarter-turns CW around Y, same convention as PlacedRoom.Rotation.
        public int Rotation;

        // ── Per-slot furniture overrides (Phase 4) ─────────────────────────────
        // Each override is gated by a bool toggle so the runtime can tell whether
        // to inherit the RoomDefinition's value or use the override. Overrides
        // affect ONLY this placement in this blueprint — different blueprints with
        // the same room aren't touched.
        public bool OverrideFurnitureCountRange;
        public Vector2Int FurnitureCountRange;
        public bool OverrideFurnitureTags;
        public string[] FurnitureTags;
        public bool OverrideFurnitureRules;
        public FurnitureRule[] FurnitureRules;

        public bool HasAnyOverride =>
            OverrideFurnitureCountRange || OverrideFurnitureTags || OverrideFurnitureRules;
    }

    public enum EdgeState
    {
        // Use whatever the room sockets + ApplyDoorPolicy would produce by default.
        Default = 0,
        // Force a solid wall regardless of sockets — even if both sides have a socket.
        Wall    = 1,
        // Force an open passage (archway) — strip walls / lintel like the dining-kitchen
        // multi-cell-share treatment.
        Open    = 2,
        // Force a doored connection — like the default for private-room connections.
        Door    = 3,
    }

    // Per-edge wall override, keyed by the two cells the edge sits between.
    // Order of CellA/CellB doesn't matter; the bootstrapper normalises before lookup.
    [Serializable]
    public struct EdgeOverride
    {
        public Vector3Int CellA;
        public Vector3Int CellB;
        public EdgeState State;
    }
}
