using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Shared IMGUI controls for room-style / paint pickers, used by both the
    // F3 (3D) and F1 (2D) block editors so the look + behaviour stay identical.
    public static class BlockStyleGUI
    {
        // Variant cycler: "(random)" then every catalog prefab name of `kind`.
        // Returns true when the selection changed.
        public static bool VariantCycler(BlockPrefabCatalog cat, BlockKind kind, ref string current)
        {
            var names = cat.NamesOfKind(kind);
            GUILayout.BeginHorizontal();
            bool changed = false;
            if (GUILayout.Button("◀", GUILayout.Width(28)))
            { current = Step(names, current, -1); changed = true; }
            GUILayout.Label(string.IsNullOrEmpty(current) ? "(random)" : current,
                            GUI.skin.box, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(28)))
            { current = Step(names, current, +1); changed = true; }
            GUILayout.EndHorizontal();
            return changed;
        }

        private static string Step(List<string> names, string current, int dir)
        {
            int total = names.Count + 1;                       // slot 0 = random
            int idx = string.IsNullOrEmpty(current) ? 0 : names.IndexOf(current) + 1;
            if (idx < 0) idx = 0;
            idx = ((idx + dir) % total + total) % total;
            return idx == 0 ? "" : names[idx - 1];
        }

        // 24-swatch grid + None. Returns true when the index changed.
        public static bool SwatchGrid(ref int colorIndex)
        {
            bool changed = false;
            var prev = GUI.color;
            GUI.color = Color.white;
            if (GUILayout.Button(colorIndex < 0 ? "■ None (selected)" : "None"))
            { if (colorIndex != -1) { colorIndex = -1; changed = true; } }

            const int perRow = 6;
            for (int i = 0; i < BlockColorPalette.Presets.Length; i++)
            {
                if (i % perRow == 0) GUILayout.BeginHorizontal();
                GUI.color = BlockColorPalette.Presets[i];
                if (GUILayout.Button(colorIndex == i ? "●" : " ",
                        GUILayout.Width(30), GUILayout.Height(22)))
                { if (colorIndex != i) { colorIndex = i; changed = true; } }
                if (i % perRow == perRow - 1) GUILayout.EndHorizontal();
            }
            if (BlockColorPalette.Presets.Length % perRow != 0) GUILayout.EndHorizontal();
            GUI.color = prev;
            return changed;
        }

        // Full "Room style" block for the given label. Writes into bp.RoomStyles
        // and returns true if anything changed (caller should regen).
        public static bool RoomStyleSection(BlockBlueprintAsset bp, BlockPrefabCatalog cat,
                                             string label)
        {
            GUILayout.Space(8);
            GUILayout.Label($"Room style: {(string.IsNullOrEmpty(label) ? "<no label>" : label)}",
                            GUI.skin.box);
            if (bp == null || cat == null || string.IsNullOrEmpty(label)) return false;

            var rs = bp.GetRoomStyle(label);
            bool changed = false;
            GUILayout.Label("Wall variant");
            if (VariantCycler(cat, BlockKind.Wall, ref rs.WallPrefabName)) changed = true;
            GUILayout.Label("Wall colour");
            if (SwatchGrid(ref rs.WallColorIndex)) changed = true;
            GUILayout.Space(4);
            GUILayout.Label("Door variant");
            if (VariantCycler(cat, BlockKind.Door, ref rs.DoorPrefabName)) changed = true;
            GUILayout.Label("Door colour");
            if (SwatchGrid(ref rs.DoorColorIndex)) changed = true;

            if (changed) { rs.Label = label; bp.SetRoomStyle(rs); }
            return changed;
        }
    }
}
