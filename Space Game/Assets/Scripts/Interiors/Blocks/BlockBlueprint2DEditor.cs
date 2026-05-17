using System;
using System.Collections.Generic;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FriendSlop.Interiors.Blocks
{
    // Top-down 2D IMGUI editor for BlockBlueprintAsset — F1 toggle. Same data
    // as the F3 3D editor; the two views are alternate ways to author the same
    // blueprint. Top-down is faster for laying out floor plans; 3D is better
    // for previewing walls and doors in context.
    //
    // Gated to block-mode interiors only (same rule as F3). Pauses Time.
    public class BlockBlueprint2DEditor : MonoBehaviour
    {
        [SerializeField] private Key toggleKey = Key.F1;

        public bool IsActive { get; private set; }
        public static bool IsAnyActive => s_instance != null && s_instance.IsActive;
        private static BlockBlueprint2DEditor s_instance;

        // Editor state (mirrors the 3D editor's API so F1 + F3 feel consistent).
        public BlockBlueprintAsset Blueprint { get; private set; }
        public BlockPrefabCatalog Catalog { get; private set; }
        public InteriorSceneBootstrapper Bootstrapper { get; private set; }
        public BlockKind SelectedKind = BlockKind.Floor;
        public string StyleTag = "";
        public string TagsString = "bedroom";
        public string Label = "Bedroom";
        public int EditFloor;

        private float _previousTimeScale = 1f;
        private CursorLockMode _previousLockState;
        private bool _previousCursorVisible;

        private const int CellPixels   = 32;
        private const int PaletteWidth = 240;
        private const int ToolbarHeight = 40;
        private Vector2 _gridScroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindFirstObjectByType<BlockBlueprint2DEditor>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("[BlockBlueprint2DEditor]");
            DontDestroyOnLoad(go);
            go.AddComponent<BlockBlueprint2DEditor>();
            Debug.Log("[BlockBlueprint2DEditor] Auto-spawned. Press F1 inside a block-blueprint interior.");
        }

        private void OnEnable()  => s_instance = this;
        private void OnDisable() { if (s_instance == this) s_instance = null; if (IsActive) Close(); }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb[toggleKey].wasPressedThisFrame || kb.f1Key.wasPressedThisFrame) Toggle();
            if (!IsActive) return;

            if (Bootstrapper == null || Blueprint == null)
            {
                Close();
                return;
            }
            // Keep cursor unlocked while open — 2D IMGUI needs mouse clicks.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;

            // PageUp / PageDown switches edit floor.
            if (kb.pageUpKey.wasPressedThisFrame   || kb.rightBracketKey.wasPressedThisFrame) EditFloor++;
            if (kb.pageDownKey.wasPressedThisFrame || kb.leftBracketKey.wasPressedThisFrame)  EditFloor--;
        }

        private void Toggle()
        {
            if (IsActive) { Close(); return; }
            if (!CanOpen()) return;
            Open();
        }

        private bool CanOpen()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && nm.ConnectedClientsIds.Count > 1)
            {
                Debug.Log("[BlockBlueprint2DEditor] Multiplayer session active — refusing to open. Solo/host-only dev tool.");
                return false;
            }
            Bootstrapper = FindFirstObjectByType<InteriorSceneBootstrapper>(FindObjectsInactive.Include);
            if (Bootstrapper == null || !Bootstrapper.IsBlockMode)
            {
                Debug.LogWarning("[BlockBlueprint2DEditor] No block-mode interior in scene. Walk into a residential building first.");
                return false;
            }
            Blueprint = InteriorSessionData.BlockBlueprint;
            Catalog   = Bootstrapper.CurrentDefinition != null
                ? Bootstrapper.CurrentDefinition.BlockCatalog : null;
            if (Blueprint == null || Catalog == null)
            {
                Debug.LogWarning("[BlockBlueprint2DEditor] Block blueprint or catalog missing on BuildingDefinition.");
                return false;
            }
            return true;
        }

        private void Open()
        {
            IsActive = true;
            _previousTimeScale = Time.timeScale;
            _previousLockState = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            Time.timeScale = 0f;
            NetworkFirstPersonController.UseUnscaledOwnerTime = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        private void Close()
        {
            IsActive = false;
            NetworkFirstPersonController.UseUnscaledOwnerTime = false;
            // Force-restore to running. These editors are the only things that
            // pause for block editing; restoring a captured _previousTimeScale
            // of 0 (e.g. F3 was open underneath) would leave the game frozen
            // and "stuck — can't move". 3D editor still active → leave it to
            // own the pause.
            Time.timeScale   = BlockBlueprint3DEditor.IsAnyActive ? 0f : 1f;
            Cursor.lockState = _previousLockState;
            Cursor.visible   = _previousCursorVisible;
            #if UNITY_EDITOR
            if (Blueprint != null) { EditorUtility.SetDirty(Blueprint); AssetDatabase.SaveAssets(); }
            #endif
            // Building geometry may have been rebuilt around the player while
            // editing top-down — force them to a known-safe spot so they don't
            // close F1 stuck inside a wall (unconditional, not just if fallen).
            if (Bootstrapper != null && Bootstrapper.IsServer)
                Bootstrapper.ForceTeleportPlayersToSafe();
            Bootstrapper = null;
            Blueprint    = null;
            Catalog      = null;
        }

        // ─── Rendering ─────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!IsActive || Blueprint == null) return;
            // Solid backdrop so the game's UI doesn't bleed through.
            GUI.color = new Color(0.07f, 0.08f, 0.11f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), WhiteTex);
            GUI.color = Color.white;

            DrawToolbar();
            DrawPalette();
            DrawGrid();
        }

        private void DrawToolbar()
        {
            var rect = new Rect(0, 0, Screen.width, ToolbarHeight);
            FillRect(rect, new Color(0.13f, 0.14f, 0.18f));
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Block Editor (2D)  —  {Blueprint.DisplayName}", GUILayout.Width(320));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Floor {EditFloor}");
            if (GUILayout.Button("◀", GUILayout.Width(28))) EditFloor--;
            if (GUILayout.Button("▶", GUILayout.Width(28))) EditFloor++;
            if (GUILayout.Button("Close (F1)", GUILayout.Width(100))) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private Vector2 _paletteScroll;
        private void DrawPalette()
        {
            var rect = new Rect(0, ToolbarHeight, PaletteWidth, Screen.height - ToolbarHeight);
            FillRect(rect, new Color(0.10f, 0.11f, 0.14f));
            GUILayout.BeginArea(rect);

            GUILayout.Label("Block kind", GUI.skin.box);
            _paletteScroll = GUILayout.BeginScrollView(_paletteScroll, GUILayout.Height(180));
            foreach (BlockKind kind in Enum.GetValues(typeof(BlockKind)))
            {
                bool sel = kind == SelectedKind;
                var prev = GUI.color;
                if (sel) GUI.color = Color.cyan;
                if (GUILayout.Button(kind.ToString())) SelectedKind = kind;
                GUI.color = prev;
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("Style tag (variant filter)", GUI.skin.box);
            StyleTag = GUILayout.TextField(StyleTag ?? "");
            if (SelectedKind == BlockKind.Floor)
            {
                GUILayout.Space(6);
                GUILayout.Label("Floor tile tags (comma-separated)", GUI.skin.box);
                TagsString = GUILayout.TextField(TagsString ?? "");
                GUILayout.Label("Tile label (rooms group by this)", GUI.skin.box);
                Label = GUILayout.TextField(Label ?? "");
            }

            if (BlockStyleGUI.RoomStyleSection(Blueprint, Catalog, Label ?? "")
                && Bootstrapper != null && Bootstrapper.IsServer)
                Bootstrapper.RegenerateFromBlockBlueprintFast();

            DrawBlueprintIO();

            GUILayout.FlexibleSpace();
            GUILayout.Label("LMB on cell: place block\n" +
                            "RMB on cell: delete block\n" +
                            "Click near a cell edge:\n" +
                            "  place Wall / Door / Window\n" +
                            "PageUp/Down: edit floor\n" +
                            "F1: close",
                            GUI.skin.box);
            GUILayout.EndArea();
        }

        private string _nameField = "";
        private Vector2 _bpListScroll;
        private List<BlockBlueprintAsset> _bpListCache;

        private void DrawBlueprintIO()
        {
            GUILayout.Space(8);
            GUILayout.Label("Blueprint file", GUI.skin.box);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) BlockBlueprintIO.Save(Blueprint);
            if (GUILayout.Button("New"))
            {
                var created = BlockBlueprintIO.CreateNew(
                    string.IsNullOrWhiteSpace(_nameField) ? "Untitled" : _nameField, Blueprint);
                if (created != null)
                {
                    Blueprint = created;
                    BlockBlueprintIO.SetActive(created, Bootstrapper);
                    _bpListCache = null;
                }
            }
            GUILayout.EndHorizontal();

            _nameField = GUILayout.TextField(_nameField ?? "");
            if (GUILayout.Button("Rename to above"))
            {
                BlockBlueprintIO.Rename(Blueprint, _nameField);
                _bpListCache = null;
            }

            GUILayout.Space(4);
            GUILayout.Label("Load:", GUI.skin.box);
            _bpListCache ??= BlockBlueprintIO.ListAll();
            _bpListScroll = GUILayout.BeginScrollView(_bpListScroll, GUILayout.Height(110));
            foreach (var a in _bpListCache)
            {
                if (a == null) continue;
                bool active = a == Blueprint;
                var prev = GUI.color;
                if (active) GUI.color = Color.cyan;
                if (GUILayout.Button((active ? "▶ " : "  ") + (a.DisplayName ?? a.name)))
                {
                    Blueprint = a;
                    BlockBlueprintIO.SetActive(a, Bootstrapper);
                }
                GUI.color = prev;
            }
            GUILayout.EndScrollView();
        }

        private void DrawGrid()
        {
            var gridX = PaletteWidth + 12;
            var gridY = ToolbarHeight + 12;
            var gridW = Screen.width - PaletteWidth - 24;
            var gridH = Screen.height - ToolbarHeight - 24;
            var rect  = new Rect(gridX, gridY, gridW, gridH);
            FillRect(rect, new Color(0.08f, 0.09f, 0.11f));

            GUILayout.BeginArea(rect);
            _gridScroll = GUILayout.BeginScrollView(_gridScroll);

            const int cellsAcross = 40;
            int totalPx = cellsAcross * CellPixels;
            GUILayout.Box(GUIContent.none, GUILayout.Width(totalPx), GUILayout.Height(totalPx));
            var areaRect = GUILayoutUtility.GetLastRect();
            int half = cellsAcross / 2;

            // Mouse-cell math.
            var mouseLocal = Event.current.mousePosition - new Vector2(areaRect.x, areaRect.y);
            int hoverX = Mathf.FloorToInt(mouseLocal.x / CellPixels) - half;
            int hoverZ = half - 1 - Mathf.FloorToInt(mouseLocal.y / CellPixels);
            float cellRelX = (mouseLocal.x / CellPixels) - Mathf.FloorToInt(mouseLocal.x / CellPixels);
            float cellRelZ = (mouseLocal.y / CellPixels) - Mathf.FloorToInt(mouseLocal.y / CellPixels);

            // Grid lines.
            var lineCol = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            for (int i = 0; i <= cellsAcross; i++)
            {
                float p = i * CellPixels;
                DrawLine(new Vector2(areaRect.x, areaRect.y + p), new Vector2(areaRect.x + totalPx, areaRect.y + p), lineCol);
                DrawLine(new Vector2(areaRect.x + p, areaRect.y), new Vector2(areaRect.x + p, areaRect.y + totalPx), lineCol);
            }
            // Origin marker.
            var originPx = CellToPixel(new Vector3Int(0, 0, 0), areaRect, half);
            DrawLine(new Vector2(originPx.x, areaRect.y), new Vector2(originPx.x, areaRect.y + totalPx), new Color(1f, 0.6f, 0.2f, 0.6f));
            DrawLine(new Vector2(areaRect.x, originPx.y + CellPixels), new Vector2(areaRect.x + totalPx, originPx.y + CellPixels), new Color(1f, 0.6f, 0.2f, 0.6f));

            DrawPlacedBlocks(areaRect, half);

            // Click handling.
            if (Event.current.type == EventType.MouseDown && areaRect.Contains(Event.current.mousePosition))
            {
                var cell = new Vector3Int(hoverX, EditFloor, hoverZ);
                if (Event.current.button == 0)
                {
                    if (SelectedKind.IsEdgeMounted())
                    {
                        var edge = PickNearestEdge(cellRelX, cellRelZ);
                        PlaceEdgeBlock(cell, edge);
                    }
                    else PlaceCellBlock(cell);
                    Event.current.Use();
                }
                else if (Event.current.button == 1)
                {
                    DeleteBlocksAt(cell);
                    Event.current.Use();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private void DrawPlacedBlocks(Rect areaRect, int half)
        {
            foreach (var b in Blueprint.Blocks)
            {
                if (b.Cell.y != EditFloor) continue;
                if (b.Kind == BlockKind.Floor)
                {
                    var px = CellToPixel(b.Cell, areaRect, half);
                    var r  = new Rect(px.x + 1, px.y + 1, CellPixels - 2, CellPixels - 2);
                    var col = ColorForLabel(b.Label);
                    GUI.color = col;
                    GUI.DrawTexture(r, WhiteTex);
                    GUI.color = new Color(0.95f, 0.95f, 0.95f, 1f);
                    GUI.Label(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4),
                              string.IsNullOrEmpty(b.Label) ? b.Kind.ToString() : b.Label);
                    GUI.color = Color.white;
                }
                else if (b.Kind.IsEdgeMounted())
                {
                    DrawEdgeMarker(b, areaRect, half);
                }
            }
        }

        private void DrawEdgeMarker(BlockEntry b, Rect areaRect, int half)
        {
            var px = CellToPixel(b.Cell, areaRect, half);
            Rect r;
            const float t = 4f;
            switch (b.Edge)
            {
                case SocketDirection.North:
                    r = new Rect(px.x, px.y - t * 0.5f, CellPixels, t); break;
                case SocketDirection.South:
                    r = new Rect(px.x, px.y + CellPixels - t * 0.5f, CellPixels, t); break;
                case SocketDirection.East:
                    r = new Rect(px.x + CellPixels - t * 0.5f, px.y, t, CellPixels); break;
                case SocketDirection.West:
                    r = new Rect(px.x - t * 0.5f, px.y, t, CellPixels); break;
                default: return;
            }
            GUI.color = b.Kind == BlockKind.Door   ? new Color(0.85f, 0.55f, 0.20f)
                      : b.Kind == BlockKind.Window ? new Color(0.30f, 0.65f, 1.00f)
                      :                              new Color(0.85f, 0.85f, 0.85f);
            GUI.DrawTexture(r, WhiteTex);
            GUI.color = Color.white;
        }

        private static SocketDirection PickNearestEdge(float relX, float relZ)
        {
            float distW = relX;
            float distE = 1f - relX;
            float distN = relZ;
            float distS = 1f - relZ;
            float min = Mathf.Min(Mathf.Min(distW, distE), Mathf.Min(distN, distS));
            if (min == distN) return SocketDirection.North;
            if (min == distS) return SocketDirection.South;
            if (min == distW) return SocketDirection.West;
            return SocketDirection.East;
        }

        private void PlaceCellBlock(Vector3Int cell)
        {
            // Disallow stacking the same kind on a cell.
            for (int i = 0; i < Blueprint.Blocks.Count; i++)
                if (Blueprint.Blocks[i].Cell == cell && Blueprint.Blocks[i].Kind == SelectedKind) return;
            Blueprint.Blocks.Add(new BlockEntry
            {
                Cell = cell, Kind = SelectedKind,
                Tags  = SelectedKind == BlockKind.Floor ? ParseTags(TagsString) : null,
                Label = SelectedKind == BlockKind.Floor ? Label : null,
                StyleTag = StyleTag,
            });
            Regen();
        }

        private void PlaceEdgeBlock(Vector3Int cell, SocketDirection edge)
        {
            for (int i = 0; i < Blueprint.Blocks.Count; i++)
            {
                var b = Blueprint.Blocks[i];
                if (b.Cell == cell && b.Kind == SelectedKind && b.Edge == edge) return;
            }
            Blueprint.Blocks.Add(new BlockEntry
            {
                Cell = cell, Kind = SelectedKind, Edge = edge, StyleTag = StyleTag,
            });
            Regen();
        }

        private void DeleteBlocksAt(Vector3Int cell)
        {
            bool any = false;
            for (int i = Blueprint.Blocks.Count - 1; i >= 0; i--)
            {
                if (Blueprint.Blocks[i].Cell == cell) { Blueprint.Blocks.RemoveAt(i); any = true; }
            }
            if (any) Regen();
        }

        private void Regen()
        {
            if (Bootstrapper != null && Bootstrapper.IsServer)
                Bootstrapper.RegenerateFromBlockBlueprintFast();
        }

        private static string[] ParseTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<string>();
            var p = s.Split(',');
            for (int i = 0; i < p.Length; i++) p[i] = p[i].Trim();
            return p;
        }

        // Stable colour from label hash so two cells with the same label look
        // visually grouped in the 2D view.
        private static readonly Dictionary<string, Color> _labelColorCache = new();
        private static Color ColorForLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return new Color(0.5f, 0.5f, 0.5f);
            if (_labelColorCache.TryGetValue(label, out var c)) return c;
            int h = label.GetHashCode();
            float hue = ((uint)h % 256) / 256f;
            var col = Color.HSVToRGB(hue, 0.55f, 0.85f);
            _labelColorCache[label] = col;
            return col;
        }

        private static Vector2 CellToPixel(Vector3Int cell, Rect areaRect, int half)
        {
            // Top-left of cell.
            float x = areaRect.x + (cell.x + half) * CellPixels;
            float y = areaRect.y + (half - 1 - cell.z) * CellPixels;
            return new Vector2(x, y);
        }

        // ── IMGUI primitives ────────────────────────────────────────────────

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
        private static void FillRect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, WhiteTex);
            GUI.color = prev;
        }
        private static void DrawLine(Vector2 a, Vector2 b, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            float dx = b.x - a.x;
            float dy = b.y - a.y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            var pivot = new Vector2(a.x, a.y);
            var rect  = new Rect(a.x, a.y - 0.5f, len, 1f);
            GUIUtility.RotateAroundPivot(ang, pivot);
            GUI.DrawTexture(rect, WhiteTex);
            GUI.matrix = Matrix4x4.identity;
            GUI.color = prev;
        }
    }
}
