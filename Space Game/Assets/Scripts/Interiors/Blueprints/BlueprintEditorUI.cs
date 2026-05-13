using UnityEngine;

namespace FriendSlop.Interiors.Blueprints
{
    // IMGUI rendering for the Blueprint Editor. Phase 1: 2D top-down grid + room
    // palette + place/delete + save/load/rename/delete/new.
    // IMGUI is intentionally chosen over UGUI for MVP speed — this is a dev tool,
    // not shipping UI. We can polish to a UGUI canvas in a later pass if needed.
    [RequireComponent(typeof(BlueprintEditorController))]
    public class BlueprintEditorUI : MonoBehaviour
    {
        private const int CellPixels = 28;             // size of one grid cell in pixels
        private const int PaletteWidth = 220;
        private const int InspectorWidth = 320;        // right-side def inspector
        private const int ToolbarHeight = 48;
        private const int InspectorHeight = 110;

        private BlueprintEditorController _ctrl;
        private Vector2 _paletteScroll;
        private Vector2 _savedScroll;
        private Vector2 _gridScroll;
        private string _newName = "Blueprint";
        private string _renameBuffer = "";
        private int _editFloor;
        private bool _edgeMode;
        // Palette filter: empty string = show all; otherwise show rooms whose name
        // begins with "Room_<Filter>_". Buttons populated from the actual palette.
        private string _buildingFilter = "";

        private void Awake() => _ctrl = GetComponent<BlueprintEditorController>();

        private void OnGUI()
        {
            if (_ctrl == null || !_ctrl.IsActive) return;

            // Solid backdrop covers the whole screen so the game's UGUI canvas
            // (round HUD, lobby buttons, etc.) doesn't bleed through.
            var prevColor = GUI.color;
            GUI.color = new Color(0.06f, 0.07f, 0.10f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), WhiteTex);
            GUI.color = prevColor;

            DrawToolbar();
            DrawPalette();
            DrawGrid();
            DrawInspector();
            DrawDefinitionPanel();
        }

        // ── Toolbar (top) ──────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            var rect = new Rect(0, 0, Screen.width, ToolbarHeight);
            FillRect(rect, new Color(0.13f, 0.14f, 0.18f));
            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();

            GUILayout.Label("Blueprint Editor", GUILayout.Width(140));

            _newName = GUILayout.TextField(_newName, GUILayout.Width(160));
            if (GUILayout.Button("New", GUILayout.Width(50))) _ctrl.NewBlueprint(_newName);

            GUILayout.Space(12);
            GUI.enabled = _ctrl.Current != null;
            if (GUILayout.Button("Save", GUILayout.Width(50))) _ctrl.Save();
            _renameBuffer = GUILayout.TextField(_renameBuffer, GUILayout.Width(140));
            if (GUILayout.Button("Rename", GUILayout.Width(70))) _ctrl.Rename(_renameBuffer);
            if (GUILayout.Button("Delete", GUILayout.Width(60))) _ctrl.DeleteCurrent();
            GUI.enabled = true;

            GUILayout.Space(20);
            GUILayout.Label($"Current: {(_ctrl.Current ? _ctrl.Current.DisplayName : "<none>")}");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Floor: {_editFloor}");
            if (GUILayout.Button("◀", GUILayout.Width(30))) _editFloor--;
            if (GUILayout.Button("▶", GUILayout.Width(30))) _editFloor++;
            GUILayout.Label($"Rot: {_ctrl.PlaceRotation * 90}°");
            if (GUILayout.Button("Q", GUILayout.Width(30))) _ctrl.RotateCcw();
            if (GUILayout.Button("E", GUILayout.Width(30))) _ctrl.RotateCw();
            _edgeMode = GUILayout.Toggle(_edgeMode, "Edge Mode", GUI.skin.button, GUILayout.Width(90));
            GUI.enabled = _ctrl.Current != null;
            if (GUILayout.Button("Refresh (F2)", GUILayout.Width(110))) _ctrl.RefreshTestBuilding();
            if (GUILayout.Button("Clear Preview", GUILayout.Width(110))) _ctrl.ClearTestBuilding();
            GUI.enabled = true;
            if (GUILayout.Button("Close (F1)", GUILayout.Width(100))) _ctrl.Toggle();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ── Palette + saved blueprints (left sidebar) ─────────────────────────

        private void DrawPalette()
        {
            var rect = new Rect(0, ToolbarHeight, PaletteWidth, Screen.height - ToolbarHeight);
            FillRect(rect, new Color(0.10f, 0.11f, 0.14f));
            GUILayout.BeginArea(rect);

            GUILayout.Label("Saved Blueprints", GUI.skin.box);
            _savedScroll = GUILayout.BeginScrollView(_savedScroll, GUILayout.Height(160));
            foreach (var bp in _ctrl.SavedBlueprints)
            {
                if (bp == null) continue;
                if (GUILayout.Button($"{(bp == _ctrl.Current ? "▶ " : "  ")}{bp.DisplayName}"))
                {
                    _ctrl.Load(bp);
                    _renameBuffer = bp.DisplayName;
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.Label("Room Palette", GUI.skin.box);

            // Filter row — derive distinct building tags from the palette.
            DrawBuildingFilters();

            _paletteScroll = GUILayout.BeginScrollView(_paletteScroll);
            foreach (var def in _ctrl.Palette)
            {
                if (def == null) continue;
                if (!string.IsNullOrEmpty(_buildingFilter)
                    && GetBuildingTag(def.name) != _buildingFilter) continue;
                bool selected = def == _ctrl.SelectedRoomDef;
                var prevColor = GUI.color;
                if (selected) GUI.color = Color.cyan;
                if (GUILayout.Button($"{def.name}\n{def.GridSize.x}×{def.GridSize.y}"))
                {
                    _ctrl.SelectedRoomDef = def;
                }
                GUI.color = prevColor;
            }
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        // ── Grid (centre, click-to-place / right-click-to-delete) ─────────────

        private void DrawGrid()
        {
            var gridX = PaletteWidth + 12;
            var gridY = ToolbarHeight + 12;
            var gridW = Screen.width - PaletteWidth - InspectorWidth - 24;
            var gridH = Screen.height - ToolbarHeight - InspectorHeight - 24;
            var rect  = new Rect(gridX, gridY, gridW, gridH);
            FillRect(rect, new Color(0.08f, 0.09f, 0.11f));

            GUILayout.BeginArea(rect);
            _gridScroll = GUILayout.BeginScrollView(_gridScroll);

            // Pixel-space drawing area sized to ~40×40 cells around origin so we
            // can pan via the scroll view.
            const int cellsAcross = 40;
            int totalPx = cellsAcross * CellPixels;
            GUILayout.Box(GUIContent.none, GUILayout.Width(totalPx), GUILayout.Height(totalPx));
            var areaRect = GUILayoutUtility.GetLastRect();

            // Convert mouse position (in scroll-view local space) to a grid cell.
            // X in [-cellsAcross/2 .. +cellsAcross/2], Z same.
            int half = cellsAcross / 2;
            var mouseLocal = Event.current.mousePosition - new Vector2(areaRect.x, areaRect.y);
            int hoverX = Mathf.FloorToInt(mouseLocal.x / CellPixels) - half;
            int hoverZ = half - 1 - Mathf.FloorToInt(mouseLocal.y / CellPixels);   // invert Y to match world Z

            // Draw grid lines.
            var lineCol = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            for (int i = 0; i <= cellsAcross; i++)
            {
                float p = i * CellPixels;
                DrawLine(new Vector2(areaRect.x, areaRect.y + p), new Vector2(areaRect.x + totalPx, areaRect.y + p), lineCol);
                DrawLine(new Vector2(areaRect.x + p, areaRect.y), new Vector2(areaRect.x + p, areaRect.y + totalPx), lineCol);
            }

            // Origin cross-hair so the user can find (0,0,0).
            var originPx = CellToPixel(new Vector3Int(0, 0, 0), areaRect, half);
            DrawLine(new Vector2(originPx.x, areaRect.y), new Vector2(originPx.x, areaRect.y + totalPx), new Color(1f, 0.6f, 0.2f, 0.6f));
            DrawLine(new Vector2(areaRect.x, originPx.y + CellPixels), new Vector2(areaRect.x + totalPx, originPx.y + CellPixels), new Color(1f, 0.6f, 0.2f, 0.6f));

            // Draw placed rooms on the current floor.
            if (_ctrl.Current != null)
            {
                foreach (var room in _ctrl.Current.Rooms)
                {
                    if (room.GridPosition.y != _editFloor) continue;
                    DrawRoom(room, areaRect, half);
                }
            }

            // Edge overlays (only when there's a current blueprint).
            if (_ctrl.Current != null)
                DrawEdges(areaRect, half, cellsAcross);

            // Hover preview for the selected room at the hovered cell (place mode only).
            // mousePosition is in the same coordinate system as areaRect (both adjusted by
            // BeginArea + BeginScrollView), so check directly against areaRect.
            if (!_edgeMode && _ctrl.SelectedRoomDef != null && _ctrl.Current != null
                && areaRect.Contains(Event.current.mousePosition))
            {
                var hoverCell = new Vector3Int(hoverX, _editFloor, hoverZ);
                bool conflict = _ctrl.CellsOccupied(_ctrl.SelectedRoomDef, hoverCell, _ctrl.PlaceRotation);
                DrawHoverPreview(_ctrl.SelectedRoomDef, hoverCell, _ctrl.PlaceRotation, areaRect, half, conflict);
            }

            // Click handling — placement / delete (place mode), or edge cycle (edge mode).
            if (Event.current.type == EventType.MouseDown && areaRect.Contains(Event.current.mousePosition))
            {
                var cell = new Vector3Int(hoverX, _editFloor, hoverZ);
                if (_edgeMode && Event.current.button == 0)
                {
                    if (TryPickEdge(Event.current.mousePosition, areaRect, half,
                                    out var ea, out var eb))
                    {
                        _ctrl.CycleEdgeState(ea, eb);
                        Event.current.Use();
                    }
                }
                else if (Event.current.button == 0)
                {
                    // Click on a placed room → select it for editing in the inspector.
                    // Click on an empty cell with a palette selection → place.
                    int existing = _ctrl.FindPlacementAt(cell);
                    if (existing >= 0)
                    {
                        _ctrl.SelectedPlacementIndex = existing;
                    }
                    else if (_ctrl.SelectedRoomDef != null)
                    {
                        _ctrl.PlaceRoom(_ctrl.SelectedRoomDef, cell, _ctrl.PlaceRotation);
                    }
                    Event.current.Use();
                }
                else if (Event.current.button == 1)
                {
                    int existing = _ctrl.FindPlacementAt(cell);
                    _ctrl.DeleteRoomAt(cell);
                    if (existing == _ctrl.SelectedPlacementIndex)
                        _ctrl.SelectedPlacementIndex = -1;
                    else if (existing >= 0 && existing < _ctrl.SelectedPlacementIndex)
                        _ctrl.SelectedPlacementIndex--;  // index shift after RemoveAt
                    Event.current.Use();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawRoom(RoomPlacement room, Rect areaRect, int half)
        {
            var size = (room.Rotation & 1) == 0
                ? room.Definition.GridSize
                : new Vector2Int(room.Definition.GridSize.y, room.Definition.GridSize.x);
            var px = CellToPixel(new Vector3Int(room.GridPosition.x,
                                                 room.GridPosition.y,
                                                 room.GridPosition.z + size.y - 1),
                                  areaRect, half);
            var rect = new Rect(px.x + 1, px.y + 1,
                                size.x * CellPixels - 2, size.y * CellPixels - 2);
            var col = ColorForRoom(room.Definition);
            // Solid fill so the block is actually visible against the dark grid.
            var prev = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(rect, WhiteTex);
            // Darker outline so adjacent rooms read as distinct blocks.
            GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
            DrawLine(new Vector2(rect.x, rect.y), new Vector2(rect.xMax, rect.y), GUI.color);
            DrawLine(new Vector2(rect.xMax, rect.y), new Vector2(rect.xMax, rect.yMax), GUI.color);
            DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.x, rect.yMax), GUI.color);
            DrawLine(new Vector2(rect.x, rect.yMax), new Vector2(rect.x, rect.y), GUI.color);
            GUI.color = prev;
            // Label uses a dark text style so it reads on the bright fill.
            var labelStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.05f, 0.05f, 0.05f) }, fontSize = 10, wordWrap = true };
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 4),
                $"{PrettyRoomName(room.Definition.name)}\nr{room.Rotation}", labelStyle);
        }

        private void DrawBuildingFilters()
        {
            // Derive distinct tags from the current palette (so newly-added buildings
            // appear automatically). Always include "All" as the first option.
            var tags = new System.Collections.Generic.List<string>();
            tags.Add("");  // empty = All
            foreach (var def in _ctrl.Palette)
            {
                if (def == null) continue;
                var t = GetBuildingTag(def.name);
                if (!tags.Contains(t)) tags.Add(t);
            }
            tags.Sort((a, b) => string.CompareOrdinal(a, b));

            GUILayout.BeginHorizontal();
            int colsThisRow = 0;
            foreach (var tag in tags)
            {
                if (colsThisRow >= 3) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); colsThisRow = 0; }
                bool selected = _buildingFilter == tag;
                var prev = GUI.color;
                if (selected) GUI.color = Color.cyan;
                var label = string.IsNullOrEmpty(tag) ? "All" : tag;
                if (GUILayout.Button(label, GUILayout.Width(64)))
                    _buildingFilter = tag;
                GUI.color = prev;
                colsThisRow++;
            }
            GUILayout.EndHorizontal();
        }

        // "Room_Residential_Bedroom_2x2" → "Residential"
        // "Room_Stair_1x1" → "Stair"
        // "Room_Generic_2x2" → "Generic"
        private static string GetBuildingTag(string roomName)
        {
            if (string.IsNullOrEmpty(roomName)) return "Other";
            if (!roomName.StartsWith("Room_")) return "Other";
            int second = roomName.IndexOf('_', 5);
            if (second <= 5) return "Other";
            return roomName.Substring(5, second - 5);
        }

        // "Room_Residential_Bedroom_2x2" → "Bedroom 2x2" (drops the building+prefix
        // for in-cell labels so a small block can still show useful text).
        private static string PrettyRoomName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (!raw.StartsWith("Room_")) return raw;
            int second = raw.IndexOf('_', 5);
            if (second < 0) return raw.Substring(5);
            return raw.Substring(second + 1);
        }

        // Draws an overlay strip on every grid edge that has a non-Default override.
        // In edge-mode, also faintly highlights every edge between two cells where
        // at least one side is occupied (to show clickable targets).
        private void DrawEdges(Rect areaRect, int half, int cellsAcross)
        {
            var bp = _ctrl.Current;
            if (bp == null) return;
            const float thickness = 4f;

            for (int i = 0; i < bp.EdgeOverrides.Count; i++)
            {
                var e = bp.EdgeOverrides[i];
                if (e.CellA.y != _editFloor || e.CellB.y != _editFloor) continue;
                DrawOneEdge(e.CellA, e.CellB, areaRect, half, thickness, ColorForEdge(e.State));
            }

            if (_edgeMode)
            {
                // Faint hover hint for every edge that touches at least one placed room.
                var occupied = new System.Collections.Generic.HashSet<Vector3Int>();
                foreach (var room in bp.Rooms)
                {
                    if (room.GridPosition.y != _editFloor) continue;
                    var size = (room.Rotation & 1) == 0
                        ? room.Definition.GridSize
                        : new Vector2Int(room.Definition.GridSize.y, room.Definition.GridSize.x);
                    for (int x = 0; x < size.x; x++)
                    for (int z = 0; z < size.y; z++)
                        occupied.Add(new Vector3Int(room.GridPosition.x + x, _editFloor, room.GridPosition.z + z));
                }
                foreach (var cell in occupied)
                {
                    var north = new Vector3Int(cell.x, cell.y, cell.z + 1);
                    var east  = new Vector3Int(cell.x + 1, cell.y, cell.z);
                    if (_ctrl.GetEdgeState(cell, north) == EdgeState.Default)
                        DrawOneEdge(cell, north, areaRect, half, 2f, new Color(1f, 1f, 1f, 0.15f));
                    if (_ctrl.GetEdgeState(cell, east) == EdgeState.Default)
                        DrawOneEdge(cell, east, areaRect, half, 2f, new Color(1f, 1f, 1f, 0.15f));
                }
            }
        }

        private void DrawOneEdge(Vector3Int a, Vector3Int b, Rect areaRect, int half, float thickness, Color color)
        {
            // Ensure consistent ordering for orientation detection.
            var lo = a; var hi = b;
            if (hi.x < lo.x || hi.z < lo.z) { var t = lo; lo = hi; hi = t; }
            var loPx = CellToPixel(new Vector3Int(lo.x, lo.y, lo.z), areaRect, half);
            // CellToPixel returns the TOP-LEFT of the cell. The edge between (lo) and (hi)
            // sits on lo's east side (if hi.x = lo.x+1) or lo's north side (if hi.z = lo.z+1).
            Rect r;
            if (hi.x == lo.x + 1)
                r = new Rect(loPx.x + CellPixels - thickness * 0.5f, loPx.y, thickness, CellPixels);
            else
                r = new Rect(loPx.x, loPx.y - thickness * 0.5f, CellPixels, thickness);
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, WhiteTex);
            GUI.color = prev;
        }

        // Hit-test the mouse against grid edges. Returns the cell pair if within
        // a small click tolerance of an edge.
        private bool TryPickEdge(Vector2 mouse, Rect areaRect, int half,
                                  out Vector3Int a, out Vector3Int b)
        {
            const float tol = 8f;
            a = default; b = default;
            float lx = mouse.x - areaRect.x;
            float ly = mouse.y - areaRect.y;
            int cellX = Mathf.FloorToInt(lx / CellPixels) - half;
            int cellY = half - 1 - Mathf.FloorToInt(ly / CellPixels);
            float relX = lx - (cellX + half) * CellPixels;
            float relY = ly - (half - 1 - cellY) * CellPixels;
            // East edge: relX near CellPixels. West edge: relX near 0.
            // North edge: relY near 0. South edge: relY near CellPixels.
            if (relX > CellPixels - tol)
            { a = new Vector3Int(cellX, _editFloor, cellY); b = new Vector3Int(cellX + 1, _editFloor, cellY); return true; }
            if (relX < tol)
            { a = new Vector3Int(cellX - 1, _editFloor, cellY); b = new Vector3Int(cellX, _editFloor, cellY); return true; }
            if (relY < tol)
            { a = new Vector3Int(cellX, _editFloor, cellY); b = new Vector3Int(cellX, _editFloor, cellY + 1); return true; }
            if (relY > CellPixels - tol)
            { a = new Vector3Int(cellX, _editFloor, cellY - 1); b = new Vector3Int(cellX, _editFloor, cellY); return true; }
            return false;
        }

        private static Color ColorForEdge(EdgeState s)
        {
            return s switch
            {
                EdgeState.Wall    => new Color(0.95f, 0.95f, 0.95f, 0.95f),
                EdgeState.Open   => new Color(0.30f, 0.85f, 0.30f, 0.85f),
                EdgeState.Door    => new Color(0.85f, 0.55f, 0.20f, 0.90f),
                _                  => new Color(1f, 1f, 1f, 0.10f),
            };
        }

        private void DrawHoverPreview(RoomDefinition def, Vector3Int cell, int rot,
                                       Rect areaRect, int half, bool conflict)
        {
            var size = (rot & 1) == 0 ? def.GridSize
                                       : new Vector2Int(def.GridSize.y, def.GridSize.x);
            var px = CellToPixel(new Vector3Int(cell.x, cell.y, cell.z + size.y - 1), areaRect, half);
            var rect = new Rect(px.x, px.y, size.x * CellPixels, size.y * CellPixels);
            var prev = GUI.color;
            GUI.color = conflict ? new Color(1f, 0.2f, 0.2f, 0.4f) : new Color(0.4f, 1f, 0.4f, 0.4f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = prev;
        }

        // ── Definition panel (right, Phase 2) ─────────────────────────────────

        private Vector2 _defScroll;
        private bool _browseOpen;
        private Vector2 _browseScroll;
        // Mirror state — what the user is editing right now. Applied to the asset
        // on Save. Re-synced when the selected def changes.
        private RoomDefinition _defMirror;
        private Vector2Int _defGridSize;
        private int _defWeight;
        private RoomKind _defKind;
        private RoomCategory _defCategory;
        private FloorRestriction _defFloorRestriction;
        private bool[] _defSockets = new bool[6]; // N,S,E,W,Up,Down
        private bool _defIsVerticalConnector;
        private bool _defIsEntryCandidate;
        private int _defMaxCount;
        private int _defMaxHorizontalConnections;
        private Vector2Int _defFurnitureCountRange;
        private System.Collections.Generic.List<string> _defFurnitureTags = new();
        private System.Collections.Generic.List<FurnitureRule> _defFurnitureRules = new();
        private string _newRuleKind = "";
        private static string[] _knownTagsCache;

        private void DrawDefinitionPanel()
        {
            var rect = new Rect(Screen.width - InspectorWidth, ToolbarHeight,
                                InspectorWidth, Screen.height - ToolbarHeight);
            FillRect(rect, new Color(0.10f, 0.11f, 0.14f));
            GUILayout.BeginArea(rect);

            GUILayout.Label("Definition Inspector", GUI.skin.box);

            // Browse toggle — shows the full RoomDefinition picker so users can edit
            // a def even if it isn't currently placed in the blueprint.
            GUILayout.BeginHorizontal();
            _browseOpen = GUILayout.Toggle(_browseOpen, "Browse Definitions", GUI.skin.button);
            if (_ctrl.SelectedStandaloneDef != null
                && GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                _ctrl.SelectedStandaloneDef = null;
            }
            GUILayout.EndHorizontal();

            if (_browseOpen) DrawBrowsePicker();

            var def = _ctrl.SelectedDefinition;
            if (def == null)
            {
                GUILayout.Label("Click a placed room to inspect / edit its " +
                                "definition, or open Browse to pick any room. " +
                                "Edits write back to the .asset and regenerate " +
                                "the .prefab live.");
                GUILayout.EndArea();
                return;
            }

            // Re-sync mirror when the selected def changes.
            if (def != _defMirror) SyncMirrorFromDef(def);

            _defScroll = GUILayout.BeginScrollView(_defScroll);

            // Phase 4 — when a PLACED room is selected (not a browsed standalone
            // def), render the per-slot overrides block. These edits affect only
            // this placement in this blueprint.
            if (_ctrl.SelectedPlacementIndex >= 0 && _ctrl.SelectedStandaloneDef == null)
                DrawPlacementOverrides();

            GUILayout.Label($"Asset: {def.name}");
            GUILayout.Label($"Path:  {AssetPath(def)}", GUI.skin.box);

            GUILayout.Space(6);
            GUILayout.Label("Grid Size");
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(14));
            _defGridSize.x = IntField(_defGridSize.x, GUILayout.Width(40));
            GUILayout.Label("Y", GUILayout.Width(14));
            _defGridSize.y = IntField(_defGridSize.y, GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"Weight: {_defWeight}");
            _defWeight = Mathf.RoundToInt(GUILayout.HorizontalSlider(_defWeight, 1, 100));

            GUILayout.Space(6);
            GUILayout.Label("Category");
            _defCategory = EnumGrid(_defCategory);

            GUILayout.Space(6);
            GUILayout.Label("Kind");
            _defKind = EnumGrid(_defKind);

            GUILayout.Space(6);
            GUILayout.Label("Floor Restriction");
            _defFloorRestriction = EnumGrid(_defFloorRestriction);

            GUILayout.Space(6);
            GUILayout.Label("Sockets");
            string[] sLabels = { "N", "S", "E", "W", "Up", "Down" };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 6; i++)
                _defSockets[i] = GUILayout.Toggle(_defSockets[i], sLabels[i],
                    GUI.skin.button, GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            _defIsVerticalConnector = GUILayout.Toggle(_defIsVerticalConnector, "Is Vertical Connector");
            _defIsEntryCandidate    = GUILayout.Toggle(_defIsEntryCandidate,    "Is Entry Candidate");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Max Count", GUILayout.Width(120));
            _defMaxCount = IntField(_defMaxCount, GUILayout.Width(50));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Max H Connections", GUILayout.Width(120));
            _defMaxHorizontalConnections = IntField(_defMaxHorizontalConnections, GUILayout.Width(50));
            GUILayout.EndHorizontal();
            GUILayout.Label("(use -1 for unlimited)", GUI.skin.box);

            GUILayout.Space(6);
            GUILayout.Label("Furniture Count Range");
            GUILayout.BeginHorizontal();
            GUILayout.Label("min", GUILayout.Width(28));
            _defFurnitureCountRange.x = IntField(_defFurnitureCountRange.x, GUILayout.Width(40));
            GUILayout.Label("max", GUILayout.Width(28));
            _defFurnitureCountRange.y = IntField(_defFurnitureCountRange.y, GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawTagsEditor();
            GUILayout.Space(6);
            DrawRulesEditor();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save & Regenerate", GUILayout.Height(36)))
                ApplyMirrorToDef(def);
            if (GUILayout.Button("Revert", GUILayout.Width(80), GUILayout.Height(36)))
                SyncMirrorFromDef(def);
            GUILayout.EndHorizontal();

            // Phase 5 — variants. Quick-make a sibling RoomDefinition in the same
            // family. Shares prefab geometry with the original; only furniture
            // pools differ. After save the new variant joins the spawn-time pick
            // automatically (BlueprintLayoutBuilder picks one of the family per
            // spawn).
            GUILayout.Space(6);
            GUILayout.Label("Variants", GUI.skin.box);
            string family = RoomVariants.GetFamilyName(def.name);
            GUILayout.Label($"Family: {family}");
            GUILayout.BeginHorizontal();
            GUILayout.Label("New suffix:", GUILayout.Width(80));
            _newVariantSuffix = GUILayout.TextField(_newVariantSuffix, GUILayout.Width(40));
            if (GUILayout.Button("Save as Variant", GUILayout.Width(120)))
            {
                var newDef = _ctrl.SaveAsVariant(def, _newVariantSuffix);
                if (newDef != null)
                {
                    _ctrl.SelectedStandaloneDef = newDef;  // jump to editing it
                    SyncMirrorFromDef(newDef);
                    _newVariantSuffix = "";
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("Deselect"))
            {
                _ctrl.SelectedPlacementIndex = -1;
                _ctrl.SelectedStandaloneDef  = null;
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── Per-slot placement overrides (Phase 4) ────────────────────────────

        private void DrawPlacementOverrides()
        {
            if (_ctrl.Current == null) return;
            int idx = _ctrl.SelectedPlacementIndex;
            if (idx < 0 || idx >= _ctrl.Current.Rooms.Count) return;
            var p = _ctrl.Current.Rooms[idx];

            GUILayout.Label("PLACEMENT OVERRIDES (this slot, this blueprint)", GUI.skin.box);
            GUILayout.Label("Toggle each override to set a per-slot value. " +
                            "Disabled overrides inherit from the RoomDefinition below.");

            // Count range
            p.OverrideFurnitureCountRange = GUILayout.Toggle(
                p.OverrideFurnitureCountRange, "Override Furniture Count Range");
            if (p.OverrideFurnitureCountRange)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("min", GUILayout.Width(28));
                p.FurnitureCountRange.x = IntField(p.FurnitureCountRange.x, GUILayout.Width(40));
                GUILayout.Label("max", GUILayout.Width(28));
                p.FurnitureCountRange.y = IntField(p.FurnitureCountRange.y, GUILayout.Width(40));
                GUILayout.EndHorizontal();
            }

            // Tags
            GUILayout.Space(4);
            p.OverrideFurnitureTags = GUILayout.Toggle(p.OverrideFurnitureTags, "Override Furniture Tags");
            if (p.OverrideFurnitureTags)
            {
                EnsureKnownTagsCache();
                var current = new System.Collections.Generic.List<string>(p.FurnitureTags ?? System.Array.Empty<string>());
                int row = 0;
                GUILayout.BeginHorizontal();
                foreach (var t in _knownTagsCache)
                {
                    if (row >= 3) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); row = 0; }
                    bool on    = current.Contains(t);
                    bool newOn = GUILayout.Toggle(on, t, GUI.skin.button, GUILayout.Width(90));
                    if (newOn && !on) current.Add(t);
                    else if (!newOn && on) current.Remove(t);
                    row++;
                }
                GUILayout.EndHorizontal();
                p.FurnitureTags = current.ToArray();
            }

            // Rules
            GUILayout.Space(4);
            p.OverrideFurnitureRules = GUILayout.Toggle(p.OverrideFurnitureRules, "Override Furniture Rules");
            if (p.OverrideFurnitureRules)
            {
                var rules = new System.Collections.Generic.List<FurnitureRule>(p.FurnitureRules ?? System.Array.Empty<FurnitureRule>());
                int removeIdx = -1;
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    GUILayout.BeginHorizontal();
                    string kind = GUILayout.TextField(rule.Kind, GUILayout.Width(120));
                    int min     = IntField(rule.Min, GUILayout.Width(40));
                    int max     = IntField(rule.Max, GUILayout.Width(40));
                    if (GUILayout.Button("X", GUILayout.Width(24))) removeIdx = i;
                    GUILayout.EndHorizontal();
                    if (kind != rule.Kind || min != rule.Min || max != rule.Max)
                        rules[i] = new FurnitureRule(kind, min, max);
                }
                if (removeIdx >= 0) rules.RemoveAt(removeIdx);

                GUILayout.BeginHorizontal();
                _newPlacementRuleKind = GUILayout.TextField(_newPlacementRuleKind, GUILayout.Width(150));
                if (GUILayout.Button("+ Add Rule", GUILayout.Width(110)))
                {
                    if (!string.IsNullOrWhiteSpace(_newPlacementRuleKind))
                    {
                        rules.Add(new FurnitureRule(_newPlacementRuleKind.Trim(), 0, 0));
                        _newPlacementRuleKind = "";
                    }
                }
                GUILayout.EndHorizontal();
                p.FurnitureRules = rules.ToArray();
            }

            // Write the mutated struct back to the list (RoomPlacement is a struct
            // — earlier mutations modified our local copy, not the list entry).
            _ctrl.Current.Rooms[idx] = p;

            GUILayout.Space(4);
            GUILayout.Label("Definition fields (edits affect ALL uses):", GUI.skin.box);
            GUILayout.Space(4);
        }

        private string _newPlacementRuleKind = "";
        private string _newVariantSuffix = "A";

        // ── Browse picker ─────────────────────────────────────────────────────

        private void DrawBrowsePicker()
        {
            GUILayout.Label("Pick any RoomDefinition to edit:", GUI.skin.box);
            _browseScroll = GUILayout.BeginScrollView(_browseScroll, GUILayout.Height(140));
            foreach (var def in _ctrl.Palette)
            {
                if (def == null) continue;
                bool active = def == _ctrl.SelectedStandaloneDef;
                var prev = GUI.color;
                if (active) GUI.color = Color.cyan;
                if (GUILayout.Button(def.name))
                {
                    _ctrl.SelectedStandaloneDef = def;
                    _ctrl.SelectedPlacementIndex = -1;  // standalone takes precedence
                }
                GUI.color = prev;
            }
            GUILayout.EndScrollView();
        }

        // ── Tag editor ────────────────────────────────────────────────────────

        private void DrawTagsEditor()
        {
            GUILayout.Label("Furniture Tags", GUI.skin.box);
            EnsureKnownTagsCache();
            // Multi-select grid of known canonical tags.
            int cols = 3;
            int row = 0;
            GUILayout.BeginHorizontal();
            foreach (var t in _knownTagsCache)
            {
                if (row >= cols) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); row = 0; }
                bool on = _defFurnitureTags.Contains(t);
                bool newOn = GUILayout.Toggle(on, t, GUI.skin.button, GUILayout.Width(90));
                if (newOn && !on) _defFurnitureTags.Add(t);
                else if (!newOn && on) _defFurnitureTags.Remove(t);
                row++;
            }
            GUILayout.EndHorizontal();
        }

        private static void EnsureKnownTagsCache()
        {
            if (_knownTagsCache != null) return;
            var fields = typeof(FurnitureTags).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var list = new System.Collections.Generic.List<string>();
            foreach (var f in fields)
                if (f.IsLiteral && f.FieldType == typeof(string))
                    list.Add((string)f.GetRawConstantValue());
            list.Sort();
            _knownTagsCache = list.ToArray();
        }

        // ── Rule editor ───────────────────────────────────────────────────────

        private void DrawRulesEditor()
        {
            GUILayout.Label("Furniture Rules (kind / min / max)", GUI.skin.box);
            int removeIndex = -1;
            for (int i = 0; i < _defFurnitureRules.Count; i++)
            {
                var rule = _defFurnitureRules[i];
                GUILayout.BeginHorizontal();
                string kind = GUILayout.TextField(rule.Kind, GUILayout.Width(120));
                int min = IntField(rule.Min, GUILayout.Width(40));
                int max = IntField(rule.Max, GUILayout.Width(40));
                if (GUILayout.Button("X", GUILayout.Width(24))) removeIndex = i;
                GUILayout.EndHorizontal();
                if (kind != rule.Kind || min != rule.Min || max != rule.Max)
                    _defFurnitureRules[i] = new FurnitureRule(kind, min, max);
            }
            if (removeIndex >= 0) _defFurnitureRules.RemoveAt(removeIndex);

            GUILayout.BeginHorizontal();
            _newRuleKind = GUILayout.TextField(_newRuleKind, GUILayout.Width(150));
            if (GUILayout.Button("+ Add Rule", GUILayout.Width(110)))
            {
                if (!string.IsNullOrWhiteSpace(_newRuleKind))
                {
                    _defFurnitureRules.Add(new FurnitureRule(_newRuleKind.Trim(), 0, 0));
                    _newRuleKind = "";
                }
            }
            GUILayout.EndHorizontal();
        }

        private void SyncMirrorFromDef(RoomDefinition def)
        {
            _defMirror = def;
            _defGridSize = def.GridSize;
            _defWeight   = def.Weight;
            _defKind     = def.Kind;
            _defCategory = def.Category;
            _defFloorRestriction = def.FloorRestriction;
            for (int i = 0; i < 6; i++) _defSockets[i] = false;
            foreach (var s in def.Sockets)
            {
                int idx = s switch
                {
                    SocketDirection.North => 0,
                    SocketDirection.South => 1,
                    SocketDirection.East  => 2,
                    SocketDirection.West  => 3,
                    SocketDirection.Up    => 4,
                    SocketDirection.Down  => 5,
                    _                      => -1,
                };
                if (idx >= 0) _defSockets[idx] = true;
            }
            _defIsVerticalConnector       = def.IsVerticalConnector;
            _defIsEntryCandidate          = def.IsEntryCandidate;
            _defMaxCount                  = def.MaxCount;
            _defMaxHorizontalConnections  = def.MaxHorizontalConnections;
            _defFurnitureCountRange       = def.FurnitureCountRange;
            _defFurnitureTags.Clear();
            foreach (var t in def.FurnitureTags) _defFurnitureTags.Add(t);
            _defFurnitureRules.Clear();
            foreach (var r in def.FurnitureRules)
                _defFurnitureRules.Add(new FurnitureRule(r.Kind, r.Min, r.Max));
        }

        private void ApplyMirrorToDef(RoomDefinition def)
        {
            #if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(def);
            so.FindProperty("gridSize").vector2IntValue = _defGridSize;
            so.FindProperty("weight").intValue          = Mathf.Clamp(_defWeight, 1, 100);
            so.FindProperty("kind").enumValueIndex      = (int)_defKind;
            so.FindProperty("category").enumValueIndex  = (int)_defCategory;
            so.FindProperty("floorRestriction").enumValueIndex = (int)_defFloorRestriction;
            so.FindProperty("isVerticalConnector").boolValue = _defIsVerticalConnector;
            so.FindProperty("isEntryCandidate").boolValue    = _defIsEntryCandidate;
            so.FindProperty("maxCount").intValue              = Mathf.Max(0, _defMaxCount);
            so.FindProperty("maxHorizontalConnections").intValue = _defMaxHorizontalConnections;
            so.FindProperty("furnitureCountRange").vector2IntValue = _defFurnitureCountRange;
            // Sockets — write the active enum values back as a fresh array.
            var socketsProp = so.FindProperty("sockets");
            var enums = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 6; i++)
            {
                if (!_defSockets[i]) continue;
                enums.Add(i);  // SocketDirection enum order matches our mirror
            }
            socketsProp.arraySize = enums.Count;
            for (int i = 0; i < enums.Count; i++)
                socketsProp.GetArrayElementAtIndex(i).enumValueIndex = enums[i];
            // Furniture tags.
            var tagsProp = so.FindProperty("furnitureTags");
            tagsProp.arraySize = _defFurnitureTags.Count;
            for (int i = 0; i < _defFurnitureTags.Count; i++)
                tagsProp.GetArrayElementAtIndex(i).stringValue = _defFurnitureTags[i];
            // Furniture rules — array of structs with kind/min/max.
            var rulesProp = so.FindProperty("furnitureRules");
            rulesProp.arraySize = _defFurnitureRules.Count;
            for (int i = 0; i < _defFurnitureRules.Count; i++)
            {
                var elem = rulesProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("kind").stringValue = _defFurnitureRules[i].Kind ?? "";
                elem.FindPropertyRelative("min").intValue     = Mathf.Max(0, _defFurnitureRules[i].Min);
                elem.FindPropertyRelative("max").intValue     = Mathf.Max(0, _defFurnitureRules[i].Max);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            #endif
            _ctrl.SaveAndRegenerateDefinition(def);
        }

        private static int IntField(int value, params GUILayoutOption[] opts)
        {
            var s = GUILayout.TextField(value.ToString(), opts);
            return int.TryParse(s, out var v) ? v : value;
        }

        private static T EnumGrid<T>(T current) where T : System.Enum
        {
            var values = System.Enum.GetValues(typeof(T));
            int sel = -1;
            var names = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                names[i] = values.GetValue(i).ToString();
                if (current.Equals(values.GetValue(i))) sel = i;
            }
            int newSel = GUILayout.SelectionGrid(sel, names, 2,
                GUI.skin.button, GUILayout.Width(InspectorWidth - 40));
            if (newSel >= 0 && newSel < values.Length) return (T)values.GetValue(newSel);
            return current;
        }

        private static string AssetPath(UnityEngine.Object o)
        {
            #if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GetAssetPath(o);
            #else
            return o ? o.name : "<null>";
            #endif
        }

        // ── Inspector (bottom) ────────────────────────────────────────────────

        private void DrawInspector()
        {
            var rect = new Rect(PaletteWidth + 12, Screen.height - InspectorHeight,
                                Screen.width - PaletteWidth - InspectorWidth - 24, InspectorHeight - 8);
            FillRect(rect, new Color(0.13f, 0.14f, 0.18f));
            GUILayout.BeginArea(rect);
            GUILayout.Label("Place mode: left-click empty cell to place the selected room (right-click a placed room to delete). " +
                            "Q/E rotates the placement preview. Toggle 'Edge Mode' to instead click cell edges and " +
                            "cycle through Default → Wall → Open → Door overrides. F1 closes the editor.");
            int roomCount = _ctrl.Current != null ? _ctrl.Current.Rooms.Count : 0;
            int edgeCount = _ctrl.Current != null ? _ctrl.Current.EdgeOverrides.Count : 0;
            GUILayout.Label($"Rooms: {roomCount}    Edge overrides: {edgeCount}    Mode: {(_edgeMode ? "EDGE" : "PLACE")}");
            if (_ctrl.Current == null)
            {
                var prev = GUI.color; GUI.color = Color.yellow;
                GUILayout.Label("⚠ No blueprint loaded — click 'New' (top toolbar) to create one before placing rooms.");
                GUI.color = prev;
            }
            else if (!_edgeMode && _ctrl.SelectedRoomDef == null)
            {
                var prev = GUI.color; GUI.color = Color.yellow;
                GUILayout.Label("⚠ No room selected — pick one from the 'Room Palette' on the left.");
                GUI.color = prev;
            }
            GUILayout.EndArea();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Convert a grid cell to top-left pixel coordinates of that cell's drawing
        // rectangle inside the grid area.
        private static Vector2 CellToPixel(Vector3Int cell, Rect areaRect, int half)
        {
            float x = areaRect.x + (cell.x + half) * CellPixels;
            float y = areaRect.y + (half - 1 - cell.z) * CellPixels;
            return new Vector2(x, y);
        }

        private static Color ColorForRoom(RoomDefinition def)
        {
            // Stable hash → hue so each room kind reads as a distinct colour.
            int hash = def.name.GetHashCode();
            float hue = ((hash & 0x7fffffff) % 360) / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.85f);
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

        // Solid-fill helper — GUI.Box uses a translucent style that doesn't fully
        // hide what's behind it. DrawTexture with a tinted white texture gives a
        // proper opaque fill suitable for our backdrop / panel chrome.
        private static void FillRect(Rect rect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTex);
            GUI.color = prev;
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            var dir = b - a;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float len = dir.magnitude;
            var matrixOld = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y, len, 1f), WhiteTex);
            GUI.matrix = matrixOld;
            GUI.color = prev;
        }
    }
}
