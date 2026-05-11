using System.Collections.Generic;
using FriendSlop.Player;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.Interiors
{
    // Top-right debug minimap. Drawn programmatically with UI Image rects — one per room,
    // plus thin connector lines for each door, plus a player marker that tracks the
    // local player's XZ position inside the building. Filters to the current floor and
    // shows a label so the player knows where they are in a multi-storey layout.
    public class InteriorMinimap : MonoBehaviour
    {
        private const float PixelsPerMetre = 4f;
        private const float Padding        = 12f;

        private InteriorLayout _layout;
        private BuildingDefinition _def;
        private Vector3 _origin;

        private RectTransform _content;
        private RectTransform _playerMarker;
        private Text _floorLabel;
        private Text _roomLabel;

        private readonly Dictionary<int, GameObject> _floorContainers = new();
        private int _currentFloor = int.MinValue;
        private PlacedRoom _currentRoom;

        public static InteriorMinimap Spawn(InteriorLayout layout, BuildingDefinition def, Vector3 origin)
        {
            var canvasGo = new GameObject("InteriorMinimap_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var minimap = canvasGo.AddComponent<InteriorMinimap>();
            minimap.Build(layout, def, origin, canvasGo.GetComponent<RectTransform>());
            return minimap;
        }

        private void Build(InteriorLayout layout, BuildingDefinition def, Vector3 origin, RectTransform canvasRect)
        {
            _layout = layout;
            _def    = def;
            _origin = origin;

            // Compute layout extents in cell units across all floors.
            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;
            foreach (var room in layout.Rooms)
            {
                var p = room.GridPosition;
                var s = room.Definition.GridSize;
                if (p.x        < minX) minX = p.x;
                if (p.z        < minZ) minZ = p.z;
                if (p.x + s.x  > maxX) maxX = p.x + s.x;
                if (p.z + s.y  > maxZ) maxZ = p.z + s.y;
            }
            if (minX == int.MaxValue) return;

            float cellsX = (maxX - minX) * def.GridCellMeters * PixelsPerMetre;
            float cellsZ = (maxZ - minZ) * def.GridCellMeters * PixelsPerMetre;

            // Background panel anchored top-right.
            var bg = new GameObject("Background", typeof(Image));
            bg.transform.SetParent(canvasRect, false);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(1f, 1f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.pivot     = new Vector2(1f, 1f);
            bgRect.anchoredPosition = new Vector2(-12f, -12f);
            bgRect.sizeDelta = new Vector2(cellsX + Padding * 2f, cellsZ + Padding * 2f + 36f);
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            BuildHeaderLabels(bg.transform);

            // Content offsets rooms so (minX, minZ) → bottom-left of the panel.
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(bg.transform, false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(0f, 0f);
            _content.pivot     = new Vector2(0f, 0f);
            _content.anchoredPosition = new Vector2(Padding, Padding);
            _content.sizeDelta        = new Vector2(cellsX, cellsZ);

            DrawRooms(layout, def, minX, minZ);
            DrawConnections(layout, def, minX, minZ);
            DrawExitDoor(layout, def, minX, minZ);
            BuildPlayerMarker();
        }

        // Draws the building's exterior exit door as a distinct green tick on the entry
        // room's outside wall. Uses the same SW-most-cell convention as DrawConnections.
        private void DrawExitDoor(InteriorLayout layout, BuildingDefinition def, int minX, int minZ)
        {
            if (layout.ExitRoom == null || !layout.ExitSocket.HasValue) return;
            var room   = layout.ExitRoom;
            var socket = layout.ExitSocket.Value;
            var p      = room.GridPosition;
            var s      = room.Definition.GridSize;

            Vector2 a, b;
            switch (socket)
            {
                case SocketDirection.North:
                    a = new Vector2((p.x + 0.5f) - 0.4f, p.z + s.y);
                    b = new Vector2((p.x + 0.5f) + 0.4f, p.z + s.y);
                    break;
                case SocketDirection.South:
                    a = new Vector2((p.x + 0.5f) - 0.4f, p.z);
                    b = new Vector2((p.x + 0.5f) + 0.4f, p.z);
                    break;
                case SocketDirection.East:
                    a = new Vector2(p.x + s.x, (p.z + 0.5f) - 0.4f);
                    b = new Vector2(p.x + s.x, (p.z + 0.5f) + 0.4f);
                    break;
                case SocketDirection.West:
                    a = new Vector2(p.x, (p.z + 0.5f) - 0.4f);
                    b = new Vector2(p.x, (p.z + 0.5f) + 0.4f);
                    break;
                default: return; // vertical socket — no minimap representation
            }

            a = (a - new Vector2(minX, minZ)) * def.GridCellMeters * PixelsPerMetre;
            b = (b - new Vector2(minX, minZ)) * def.GridCellMeters * PixelsPerMetre;
            AddLine(GetOrCreateFloorContainer(p.y).transform,
                a, b, new Color(0.25f, 1f, 0.4f, 1f), 5f);
        }

        private void BuildHeaderLabels(Transform parent)
        {
            _roomLabel  = BuildHeaderText(parent, "RoomLabel",  yOffset: -2f,  fontSize: 13, bold: true);
            _floorLabel = BuildHeaderText(parent, "FloorLabel", yOffset: -20f, fontSize: 11, bold: false);
        }

        private static Text BuildHeaderText(Transform parent, string name, float yOffset, int fontSize, bool bold)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, yOffset);
            rt.sizeDelta = new Vector2(0f, 18f);

            var t = go.GetComponent<Text>();
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize  = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = TextAnchor.MiddleCenter;
            t.color     = Color.white;
            t.text      = "";
            return t;
        }

        private GameObject GetOrCreateFloorContainer(int floor)
        {
            if (_floorContainers.TryGetValue(floor, out var existing)) return existing;
            var go = new GameObject($"Floor_{floor}", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.SetActive(false); // Hidden until the player is on this floor.
            _floorContainers[floor] = go;
            return go;
        }

        private void DrawRooms(InteriorLayout layout, BuildingDefinition def, int minX, int minZ)
        {
            foreach (var room in layout.Rooms)
            {
                var p = room.GridPosition;
                var s = room.Definition.GridSize;
                var parent = GetOrCreateFloorContainer(p.y);

                var go = new GameObject($"Room_{p.x}_{p.z}", typeof(Image));
                go.transform.SetParent(parent.transform, false);

                var r = go.GetComponent<RectTransform>();
                r.anchorMin = new Vector2(0f, 0f);
                r.anchorMax = new Vector2(0f, 0f);
                r.pivot     = new Vector2(0f, 0f);
                r.anchoredPosition = new Vector2(
                    (p.x - minX) * def.GridCellMeters * PixelsPerMetre,
                    (p.z - minZ) * def.GridCellMeters * PixelsPerMetre);
                r.sizeDelta = new Vector2(
                    s.x * def.GridCellMeters * PixelsPerMetre - 1f,
                    s.y * def.GridCellMeters * PixelsPerMetre - 1f);

                go.GetComponent<Image>().color = ColorFor(room.Definition.Category);
            }
        }

        private void DrawConnections(InteriorLayout layout, BuildingDefinition def, int minX, int minZ)
        {
            foreach (var conn in layout.Connections)
            {
                if (conn.SocketA.IsVertical()) continue;

                // Door always sits at the SW-most cell of each wall.
                var p = conn.RoomA.GridPosition;
                var s = conn.RoomA.Definition.GridSize;
                Vector2 a, b;
                switch (conn.SocketA)
                {
                    case SocketDirection.North:
                        a = new Vector2((p.x + 0.5f) - 0.4f, p.z + s.y);
                        b = new Vector2((p.x + 0.5f) + 0.4f, p.z + s.y);
                        break;
                    case SocketDirection.South:
                        a = new Vector2((p.x + 0.5f) - 0.4f, p.z);
                        b = new Vector2((p.x + 0.5f) + 0.4f, p.z);
                        break;
                    case SocketDirection.East:
                        a = new Vector2(p.x + s.x, (p.z + 0.5f) - 0.4f);
                        b = new Vector2(p.x + s.x, (p.z + 0.5f) + 0.4f);
                        break;
                    case SocketDirection.West:
                        a = new Vector2(p.x, (p.z + 0.5f) - 0.4f);
                        b = new Vector2(p.x, (p.z + 0.5f) + 0.4f);
                        break;
                    default: continue;
                }

                a = (a - new Vector2(minX, minZ)) * def.GridCellMeters * PixelsPerMetre;
                b = (b - new Vector2(minX, minZ)) * def.GridCellMeters * PixelsPerMetre;
                AddLine(GetOrCreateFloorContainer(p.y).transform,
                    a, b, new Color(1f, 0.85f, 0.2f, 1f), 3f);
            }
        }

        private void AddLine(Transform parent, Vector2 a, Vector2 b, Color color, float thickness)
        {
            var go = new GameObject("Door", typeof(Image));
            go.transform.SetParent(parent, false);

            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 0f);
            r.pivot     = new Vector2(0.5f, 0.5f);

            var diff   = b - a;
            var length = diff.magnitude;
            r.anchoredPosition = (a + b) * 0.5f;
            r.sizeDelta        = new Vector2(Mathf.Max(length, thickness), thickness);
            r.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);

            go.GetComponent<Image>().color = color;
        }

        private void BuildPlayerMarker()
        {
            var go = new GameObject("PlayerMarker", typeof(Image));
            go.transform.SetParent(_content, false);

            _playerMarker = go.GetComponent<RectTransform>();
            _playerMarker.anchorMin = new Vector2(0f, 0f);
            _playerMarker.anchorMax = new Vector2(0f, 0f);
            _playerMarker.pivot     = new Vector2(0.5f, 0.5f);
            _playerMarker.sizeDelta = new Vector2(8f, 8f);

            var img = go.GetComponent<Image>();
            img.color = Color.red;
        }

        private void Update()
        {
            if (_playerMarker == null || _def == null) return;

            var local = LocalPlayerRegistry.Current;
            if (local == null) return;

            // Re-compute extents (cheap, runs only while minimap exists).
            int minX = int.MaxValue, minZ = int.MaxValue;
            foreach (var r in _layout.Rooms)
            {
                if (r.GridPosition.x < minX) minX = r.GridPosition.x;
                if (r.GridPosition.z < minZ) minZ = r.GridPosition.z;
            }
            if (minX == int.MaxValue) return;

            var pos = local.transform.position;
            int floor = Mathf.FloorToInt((pos.y - _origin.y) / _def.FloorHeightMeters + 0.001f);

            if (floor != _currentFloor)
            {
                _currentFloor = floor;
                foreach (var kv in _floorContainers)
                    kv.Value.SetActive(kv.Key == floor);
                if (_floorLabel != null)
                    _floorLabel.text = FloorLabelText(floor);
            }

            // Locate the room the player is standing in (XZ inside the grid footprint).
            var room = FindRoomAt(pos, floor);
            if (room != _currentRoom)
            {
                _currentRoom = room;
                if (_roomLabel != null)
                    _roomLabel.text = room != null ? PrettyRoomName(room.Definition.name) : "—";
            }

            float metresX = pos.x - _origin.x - minX * _def.GridCellMeters;
            float metresZ = pos.z - _origin.z - minZ * _def.GridCellMeters;

            _playerMarker.anchoredPosition = new Vector2(metresX, metresZ) * PixelsPerMetre;
            _playerMarker.localRotation    = Quaternion.Euler(0f, 0f, -local.transform.eulerAngles.y);
        }

        private PlacedRoom FindRoomAt(Vector3 worldPos, int floor)
        {
            float c = _def.GridCellMeters;
            float lx = (worldPos.x - _origin.x) / c;
            float lz = (worldPos.z - _origin.z) / c;
            foreach (var room in _layout.Rooms)
            {
                if (room.GridPosition.y != floor) continue;
                var p = room.GridPosition;
                var s = room.Definition.GridSize;
                if (lx < p.x || lx >= p.x + s.x) continue;
                if (lz < p.z || lz >= p.z + s.y) continue;
                return room;
            }
            return null;
        }

        // "Room_Residential_Kitchen_1x1"   → "Kitchen"
        // "Room_Residential_LivingRoom_2x2" → "Living Room"
        // "Room_Office_ManagerOffice_1x1"   → "Manager Office"
        // "Room_Stair_1x1"                  → "Stair"
        private static string PrettyRoomName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var s = raw;
            if (s.StartsWith("Room_")) s = s.Substring(5);

            // Strip NxM grid-size suffix like "_1x1", "_2x2".
            int lastUnderscore = s.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < s.Length - 1)
            {
                var suffix = s.Substring(lastUnderscore + 1);
                if (suffix.Length >= 3 && suffix[1] == 'x'
                    && char.IsDigit(suffix[0]) && char.IsDigit(suffix[2]))
                    s = s.Substring(0, lastUnderscore);
            }

            // Keep only the room-type token (the last underscore-separated word). This
            // drops the building-type prefix (Residential/Office/Factory) since every
            // room in a given building shares it — the label is more useful without it.
            int splitAt = s.LastIndexOf('_');
            if (splitAt >= 0 && splitAt < s.Length - 1)
                s = s.Substring(splitAt + 1);

            return InsertSpacesBeforeCaps(s);
        }

        // "LivingRoom" → "Living Room", "OpenPlan" → "Open Plan". Leaves single-word
        // names ("Kitchen", "Stair") untouched.
        private static string InsertSpacesBeforeCaps(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length + 4);
            sb.Append(s[0]);
            for (int i = 1; i < s.Length; i++)
            {
                if (char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private string FloorLabelText(int floor)
        {
            int delta = floor - _layout.EntryFloor;
            if (delta == 0) return "Ground (Entry)";
            if (delta < 0)  return delta == -1 ? "Basement" : $"Basement {-delta}";
            return delta == 1 ? "Upper Floor" : $"Floor +{delta}";
        }

        private static Color ColorFor(RoomCategory cat) => cat switch
        {
            RoomCategory.Entry   => new Color(0.3f, 0.7f, 1f,    0.85f),
            RoomCategory.Special => new Color(1f,   0.4f, 0.6f,  0.85f),
            RoomCategory.Utility => new Color(0.6f, 0.6f, 0.6f,  0.85f),
            _                    => new Color(0.85f, 0.85f, 0.85f, 0.85f),
        };
    }
}
