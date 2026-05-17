using System;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Left-edge IMGUI sidebar for the block editor. Block-kind buttons, tag /
    // label inputs for Floor tiles, style-tag input for variant picking,
    // edit-floor readout. Designed for use with cursor LOCKED — designer holds
    // Alt to interact with the panel.
    public class BlockBlueprint3DPaletteUI
    {
        private const float PanelWidth   = 240f;
        private const float TopMargin    = 12f;
        private const float BottomMargin = 12f;

        private readonly BlockBlueprint3DEditor _editor;
        private Vector2 _scroll;
        private Rect _panelRect;

        public bool ContainsScreenPoint(Vector2 p) => _panelRect.Contains(p);

        public BlockBlueprint3DPaletteUI(BlockBlueprint3DEditor editor)
        {
            _editor = editor;
        }

        public void OnGUI()
        {
            _panelRect = new Rect(0, TopMargin, PanelWidth, Screen.height - TopMargin - BottomMargin);
            DrawBackground(_panelRect, new Color(0.10f, 0.11f, 0.14f, 0.95f));
            GUILayout.BeginArea(_panelRect);

            GUILayout.Label("Block Editor", GUI.skin.box);
            GUILayout.Label($"Blueprint: {(_editor.Blueprint ? _editor.Blueprint.DisplayName : "<none>")}");

            GUILayout.Space(6);
            GUILayout.Label("Mode / block kind", GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(210f));
            foreach (BlockKind kind in Enum.GetValues(typeof(BlockKind)))
            {
                bool sel = !_editor.DeleteMode && kind == _editor.SelectedKind;
                var prev = GUI.color;
                if (sel) GUI.color = Color.cyan;
                if (GUILayout.Button(kind.ToString()))
                {
                    _editor.SelectedKind = kind;
                    _editor.DeleteMode = false;
                    _editor.PaintMode  = false;
                }
                GUI.color = prev;
            }
            // Dedicated Delete + Paint slots at the end of the kind list,
            // accessed via the same scroll-cycle as the kinds.
            {
                var prev = GUI.color;
                if (_editor.DeleteMode) GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("Delete")) { _editor.DeleteMode = true; _editor.PaintMode = false; }
                GUI.color = _editor.PaintMode ? new Color(0.4f, 0.8f, 1f) : Color.white;
                if (GUILayout.Button("Paint"))  { _editor.PaintMode = true; _editor.DeleteMode = false; }
                GUI.color = prev;
            }
            GUILayout.EndScrollView();

            // Style tag — applies to most blocks for variant picking.
            GUILayout.Space(6);
            GUILayout.Label("Style tag (variant filter)", GUI.skin.box);
            _editor.StyleTag = GUILayout.TextField(_editor.StyleTag ?? "");

            // Floor-only fields: tags + label
            if (_editor.SelectedKind == BlockKind.Floor)
            {
                GUILayout.Space(6);
                GUILayout.Label("Floor tile tags (comma-separated)", GUI.skin.box);
                _editor.TagsString = GUILayout.TextField(_editor.TagsString ?? "");
                GUILayout.Label("Tile label (shown in editor)", GUI.skin.box);
                _editor.Label = GUILayout.TextField(_editor.Label ?? "");
            }

            if (_editor.PaintMode) DrawPaintPicker();
            else                   DrawRoomStyle();

            DrawBlueprintIO();

            GUILayout.FlexibleSpace();
            GUILayout.Box(GUIContent.none, GUILayout.Height(1));
            GUILayout.Label($"Edit floor: {_editor.EditFloor}  (PageUp/Down)");
            GUILayout.Label($"Rotation: {_editor.Rotation * 90}°  (Q / E)");
            GUILayout.Space(6);
            string modeLine = _editor.PaintMode
                ? "PAINT mode — LMB skins the wall side\n" +
                  "you face, RMB clears it.\n"
                : _editor.DeleteMode
                ? "DELETE mode — LMB/RMB nuke the\n" +
                  "highlighted (red) block under cursor.\n"
                : "LMB: place block\n" +
                  "RMB: rotate the placement 90°\n";
            GUILayout.Label(modeLine +
                            "Scroll: cycle kind / Delete / Paint\n" +
                            "Q / E: rotate\n" +
                            "PageUp / PageDown: edit floor\n" +
                            "Hold Alt: free cursor for UI\n" +
                            "F3: exit", GUI.skin.box);
            GUILayout.EndArea();
        }

        // ── Room style + paint pickers ─────────────────────────────────────

        private void DrawRoomStyle()
        {
            if (BlockStyleGUI.RoomStyleSection(_editor.Blueprint, _editor.Catalog, _editor.Label ?? "")
                && _editor.Bootstrapper != null && _editor.Bootstrapper.IsServer)
                _editor.Bootstrapper.RegenerateFromBlockBlueprintFast();
        }

        private void DrawPaintPicker()
        {
            var cat = _editor.Catalog;
            if (cat == null) return;
            GUILayout.Space(8);
            GUILayout.Label("Paint selection", GUI.skin.box);
            GUILayout.Label("Variant (empty = keep room's)");
            string pn = _editor.PaintPrefabName;
            BlockStyleGUI.VariantCycler(cat, BlockKind.Wall, ref pn);
            _editor.PaintPrefabName = pn;
            GUILayout.Label("Colour");
            int ci = _editor.PaintColorIndex;
            BlockStyleGUI.SwatchGrid(ref ci);
            _editor.PaintColorIndex = ci;
            GUILayout.Label("Aim at a wall/door, LMB paints the\n" +
                            "side you face. RMB clears it.", GUI.skin.box);
        }

        private string _nameField = "";
        private Vector2 _bpListScroll;
        private System.Collections.Generic.List<BlockBlueprintAsset> _bpListCache;

        private void DrawBlueprintIO()
        {
            GUILayout.Space(8);
            GUILayout.Label("Blueprint file", GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) BlockBlueprintIO.Save(_editor.Blueprint);
            if (GUILayout.Button("New"))
            {
                var created = BlockBlueprintIO.CreateNew(
                    string.IsNullOrWhiteSpace(_nameField) ? "Untitled" : _nameField,
                    _editor.Blueprint);
                if (created != null) { _editor.SetBlueprint(created); _bpListCache = null; }
            }
            GUILayout.EndHorizontal();

            _nameField = GUILayout.TextField(_nameField ?? "");
            if (GUILayout.Button("Rename to above"))
            {
                BlockBlueprintIO.Rename(_editor.Blueprint, _nameField);
                _bpListCache = null;
            }

            GUILayout.Space(4);
            GUILayout.Label("Load:", GUI.skin.box);
            _bpListCache ??= BlockBlueprintIO.ListAll();
            _bpListScroll = GUILayout.BeginScrollView(_bpListScroll, GUILayout.Height(110));
            foreach (var a in _bpListCache)
            {
                if (a == null) continue;
                bool active = a == _editor.Blueprint;
                var prev = GUI.color;
                if (active) GUI.color = Color.cyan;
                if (GUILayout.Button((active ? "▶ " : "  ") + (a.DisplayName ?? a.name)))
                    _editor.SetBlueprint(a);
                GUI.color = prev;
            }
            GUILayout.EndScrollView();
        }

        private static Texture2D _whiteTex;
        private static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                }
                return _whiteTex;
            }
        }

        private static void DrawBackground(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTex);
            GUI.color = prev;
        }
    }
}
