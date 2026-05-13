#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Interiors;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private const string InteriorPrefabFolder    = "Assets/Prefabs/Interiors";
        private const string InteriorRoomFolder      = "Assets/Prefabs/Interiors/Rooms";
        private const string InteriorAssetFolder     = "Assets/Interiors";
        private const string InteriorRoomDefFolder   = "Assets/Interiors/Rooms";
        private const string InteriorBuildingFolder  = "Assets/Interiors/Buildings";

        [MenuItem("Tools/Friend Slop/Interiors/Repair Interior Assets")]
        public static void RepairInteriorAssets()
        {
            EnsureInteriorFolders();

            var roomDefs = RepairRoomDefinitions();
            var doorPrefab = RepairDoorPrefab();
            RepairBuildingDefinitions(roomDefs);
            var furnitureDefs = RepairFurnitureAssetsInternal();
            RepairInteriorCatalog(furnitureDefs);
            RepairDoorInNetworkPrefabsList(doorPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Friend Slop] Interior assets repaired.");
        }

        [MenuItem("Tools/Friend Slop/Interiors/Spawn Test Building in Scene")]
        public static void SpawnTestBuildingInScene()
        {
            var smallDef = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
                $"{InteriorBuildingFolder}/Building_Small.asset");
            var doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{InteriorPrefabFolder}/InteriorDoor.prefab");

            if (smallDef == null)
            {
                Debug.LogWarning("[Friend Slop] Run 'Repair Interior Assets' first.");
                return;
            }

            var go = new GameObject("TestBuilding");
            go.tag = "Untagged";

            // Visible exterior shell
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "Exterior";
            shell.transform.SetParent(go.transform);
            shell.transform.localScale = new Vector3(8, 8, 8);
            shell.transform.localPosition = new Vector3(4, 4, 4);
            Object.DestroyImmediate(shell.GetComponent<Collider>());

            // Trigger volume
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size   = new Vector3(8, 8, 8);
            trigger.center = new Vector3(4, 4, 4);

            var netObj = go.AddComponent<NetworkObject>();
            var spawner = go.AddComponent<InteriorSpawner>();

            var so = new SerializedObject(spawner);
            so.FindProperty("definition").objectReferenceValue = smallDef;
            so.FindProperty("doorPrefab").objectReferenceValue = doorPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Loading screen canvas
            var canvas = BuildLoadingScreenCanvas(go.transform);

            UnityEditor.Selection.activeGameObject = go;
            Debug.Log("[Friend Slop] Test building spawned. Press Play to test.");
        }

        // ── Room definitions ───────────────────────────────────────────────────

        private static RoomDefinition[] RepairRoomDefinitions()
        {
            var specs = GetRoomSpecs();
            var defs  = new RoomDefinition[specs.Length];
            for (int i = 0; i < specs.Length; i++)
                defs[i] = RepairRoomDefinition(specs[i]);
            return defs;
        }

        private static RoomDefinition RepairRoomDefinition(RoomSpec spec)
        {
            var assetPath = $"{InteriorRoomDefFolder}/{spec.Name}.asset";
            var existing  = AssetDatabase.LoadAssetAtPath<RoomDefinition>(assetPath);

            // Always rebuild the prefab — geometry rules iterate. The .asset itself can be reused.
            var prefab = RepairRoomPrefab(spec);

            if (existing != null)
            {
                // Re-sync all spec-driven fields so flag changes (e.g. EntryCandidate) take
                // effect without manually deleting the asset.
                var existingSo = new SerializedObject(existing);
                WriteRoomFields(existingSo, spec, prefab);
                existingSo.ApplyModifiedPropertiesWithoutUndo();
                return existing;
            }

            var def = ScriptableObject.CreateInstance<RoomDefinition>();
            var so  = new SerializedObject(def);
            WriteRoomFields(so, spec, prefab);
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(def, assetPath);
            return def;
        }

        private static void WriteRoomFields(SerializedObject so, RoomSpec spec, GameObject prefab)
        {
            so.FindProperty("prefab").objectReferenceValue   = prefab;
            so.FindProperty("gridSize").vector2IntValue      = spec.GridSize;
            so.FindProperty("category").enumValueIndex       = (int)spec.Category;
            so.FindProperty("kind").enumValueIndex           = (int)spec.Kind;
            so.FindProperty("isVerticalConnector").boolValue = spec.IsVerticalConnector;
            so.FindProperty("weight").intValue               = spec.Weight;
            so.FindProperty("maxHorizontalConnections").intValue = spec.MaxHorizontalConnections;
            so.FindProperty("maxCount").intValue                 = spec.MaxCount;
            so.FindProperty("isEntryCandidate").boolValue    = spec.EntryCandidate;
            so.FindProperty("floorRestriction").enumValueIndex = (int)spec.FloorRestriction;
            so.FindProperty("furnitureCountRange").vector2IntValue = spec.FurnitureCountRange;

            var socketsArr = so.FindProperty("sockets");
            socketsArr.arraySize = spec.Sockets.Length;
            for (int i = 0; i < spec.Sockets.Length; i++)
                socketsArr.GetArrayElementAtIndex(i).enumValueIndex = (int)spec.Sockets[i];

            var tagsArr = so.FindProperty("furnitureTags");
            tagsArr.arraySize = spec.FurnitureTags.Length;
            for (int i = 0; i < spec.FurnitureTags.Length; i++)
                tagsArr.GetArrayElementAtIndex(i).stringValue = spec.FurnitureTags[i];

            var rulesArr = so.FindProperty("furnitureRules");
            rulesArr.arraySize = spec.FurnitureRules.Length;
            for (int i = 0; i < spec.FurnitureRules.Length; i++)
            {
                var (kind, min, max) = spec.FurnitureRules[i];
                var elem = rulesArr.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("kind").stringValue = kind ?? "";
                elem.FindPropertyRelative("min").intValue     = Mathf.Max(0, min);
                elem.FindPropertyRelative("max").intValue     = Mathf.Max(0, max);
            }
        }

        private static GameObject RepairRoomPrefab(RoomSpec spec)
        {
            // Always overwrite — geometry rules change as we iterate the interior system,
            // and SaveAsPrefabAsset preserves the asset GUID so RoomDefinition refs survive.
            var prefabPath = $"{InteriorRoomFolder}/{spec.Name}.prefab";

            var root = new GameObject(spec.Name);
            BuildRoomGeometry(root, spec);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // Cell size for room/door/anchor geometry. Must stay in sync with
        // BuildingDefinition.gridCellMeters and InteriorSceneBootstrapper.AnchorIsOnDoorCell.
        // The grid is double-resolution — a "1×1 small room" occupies one 3.4 m cell, and
        // every legacy "1×1 room" now uses GridSize(2,2) so its physical size stays 6.8 m.
        private const float CellMetres = 3.4f;
        // The staircase room footprint is 2 cells wide in the new grid (= 6.8 m physical),
        // which keeps the ramp/hole at the original physical dimensions.
        private const int   StaircaseCells = 2;

        private static void BuildRoomGeometry(GameObject root, RoomSpec spec)
        {
            const float c = CellMetres;  // metres per grid cell
            float w = spec.GridSize.x * c;
            float d = spec.GridSize.y * c;
            const float h = 4f;
            const float wall = 0.2f;

            var sockets = new HashSet<SocketDirection>(spec.Sockets);
            bool hasUp   = sockets.Contains(SocketDirection.Up);
            bool hasDown = sockets.Contains(SocketDirection.Down);

            BuildFloorOrCeiling(root, "Floor",   y: 0f, w, d, wall, hasHole: hasDown);
            BuildFloorOrCeiling(root, "Ceiling", y: h,  w, d, wall, hasHole: hasUp);

            BuildPerimeterWall(root, SocketDirection.North, sockets, w, d, h, wall, c);
            BuildPerimeterWall(root, SocketDirection.South, sockets, w, d, h, wall, c);
            BuildPerimeterWall(root, SocketDirection.East,  sockets, w, d, h, wall, c);
            BuildPerimeterWall(root, SocketDirection.West,  sockets, w, d, h, wall, c);

            if (hasUp)
                BuildRamp(root, w, d, h, wall);

            // Generate furniture anchors — wall midpoints, corners, and centre(s).
            // Stair rooms skip anchors entirely (the staircase dominates them).
            if (!spec.IsVerticalConnector)
                BuildFurnitureAnchors(root, spec, w, d);
        }

        // Stamps FurnitureAnchor child GameObjects into the room prefab. The runtime
        // spawner uses these to place pieces, filtering out any anchor that would
        // intersect an active door's swing zone. Stair rooms and open-passage walls
        // are handled by the bootstrapper at instantiation time.
        private static void BuildFurnitureAnchors(GameObject root, RoomSpec spec, float w, float d)
        {
            // Wall midpoints — one per perimeter cell along each wall.
            // Each cell gets both a floor-Wall slot (sofa, dresser) and a WallHanging slot
            // at the same XZ — the painting hangs above whatever floor piece occupies the
            // wall. WallHanging anchors keep localPosition.y=0 because each hung furniture
            // def positions its own primitives at the right height (1.5–1.8 m).
            const float wallInset = 0.5f;       // metres from the wall (so the piece's back can touch the wall)
            // Walls are 0.2 m thick centred on the wall plane → interior face is at 0.1 m.
            // hangingInset = 0.20 puts the anchor 0.10 m INTO the room from the visible
            // surface, so wall-hung items (frames at local z ≈ -0.04 to -0.08) end up
            // proud of the wall instead of buried inside it.
            const float hangingInset = 0.20f;
            for (int cellX = 0; cellX < spec.GridSize.x; cellX++)
            {
                float cx = (cellX + 0.5f) * CellMetres;
                AddAnchor(root, $"Anchor_S_{cellX}", new Vector3(cx, 0f, wallInset),
                    AnchorPlacement.Wall, SocketDirection.South,
                    new Vector2(2.5f, 1.4f),  rotateYDeg: 0f);
                AddAnchor(root, $"Anchor_N_{cellX}", new Vector3(cx, 0f, d - wallInset),
                    AnchorPlacement.Wall, SocketDirection.North,
                    new Vector2(2.5f, 1.4f),  rotateYDeg: 180f);
                AddAnchor(root, $"Anchor_HangS_{cellX}", new Vector3(cx, 0f, hangingInset),
                    AnchorPlacement.WallHanging, SocketDirection.South,
                    new Vector2(2.5f, 0.5f),  rotateYDeg: 0f);
                AddAnchor(root, $"Anchor_HangN_{cellX}", new Vector3(cx, 0f, d - hangingInset),
                    AnchorPlacement.WallHanging, SocketDirection.North,
                    new Vector2(2.5f, 0.5f),  rotateYDeg: 180f);
            }
            for (int cellZ = 0; cellZ < spec.GridSize.y; cellZ++)
            {
                float cz = (cellZ + 0.5f) * CellMetres;
                AddAnchor(root, $"Anchor_W_{cellZ}", new Vector3(wallInset, 0f, cz),
                    AnchorPlacement.Wall, SocketDirection.West,
                    new Vector2(1.4f, 2.5f),  rotateYDeg: 90f);
                AddAnchor(root, $"Anchor_E_{cellZ}", new Vector3(w - wallInset, 0f, cz),
                    AnchorPlacement.Wall, SocketDirection.East,
                    new Vector2(1.4f, 2.5f),  rotateYDeg: 270f);
                AddAnchor(root, $"Anchor_HangW_{cellZ}", new Vector3(hangingInset, 0f, cz),
                    AnchorPlacement.WallHanging, SocketDirection.West,
                    new Vector2(0.5f, 2.5f),  rotateYDeg: 90f);
                AddAnchor(root, $"Anchor_HangE_{cellZ}", new Vector3(w - hangingInset, 0f, cz),
                    AnchorPlacement.WallHanging, SocketDirection.East,
                    new Vector2(0.5f, 2.5f),  rotateYDeg: 270f);
            }

            // Four room-corner anchors. Tall/narrow pieces (lamps, plants) live here.
            const float cornerInset = 0.6f;
            AddAnchor(root, "Anchor_Corner_SW", new Vector3(cornerInset,    0f, cornerInset),
                AnchorPlacement.Corner, SocketDirection.South, new Vector2(0.8f, 0.8f), rotateYDeg: 45f);
            AddAnchor(root, "Anchor_Corner_SE", new Vector3(w - cornerInset, 0f, cornerInset),
                AnchorPlacement.Corner, SocketDirection.South, new Vector2(0.8f, 0.8f), rotateYDeg: 315f);
            AddAnchor(root, "Anchor_Corner_NW", new Vector3(cornerInset,    0f, d - cornerInset),
                AnchorPlacement.Corner, SocketDirection.North, new Vector2(0.8f, 0.8f), rotateYDeg: 135f);
            AddAnchor(root, "Anchor_Corner_NE", new Vector3(w - cornerInset, 0f, d - cornerInset),
                AnchorPlacement.Corner, SocketDirection.North, new Vector2(0.8f, 0.8f), rotateYDeg: 225f);

            // DiningRoom uses a single dedicated anchor biased toward the kitchen-facing
            // wall (def-South — the placement constraint guarantees def-South is the side
            // that touches the Kitchen). Footprint is sized for the DiningTable (2.2×1.0)
            // plus chair clearance. Skips the generic Center anchor so nothing else
            // competes for the room-centre position.
            if (spec.Kind == RoomKind.DiningRoom)
            {
                // Anchor sits 1.5 m north of the def-south wall (= the kitchen-facing wall),
                // centred on the room's long axis. Biased toward the kitchen side as the
                // user requested, far enough off the wall that the south-side chairs
                // (~0.78 m from the table centre) still have ~0.4 m clearance to the wall.
                float diningCenterX = spec.GridSize.x * CellMetres * 0.5f;
                float diningCenterZ = Mathf.Min(1.5f, spec.GridSize.y * CellMetres * 0.5f);
                AddAnchor(root, "Anchor_DiningTable",
                    new Vector3(diningCenterX, 0f, diningCenterZ),
                    AnchorPlacement.Center, SocketDirection.North,
                    new Vector2(2.5f, 1.5f), rotateYDeg: 0f);
            }
            else
            {
                // Single room-centre anchor at the midpoint. The footprint scales with the
                // room so big rooms (LivingRoom, Garage) can host a larger Center piece (rug,
                // dining table). Cuts the anchor sprawl of per-cell Centers (3x3 room used to
                // make 9 of these) without losing room-centre placement.
                float centerFootprint = Mathf.Max(1.8f, Mathf.Min(spec.GridSize.x, spec.GridSize.y) * CellMetres * 0.6f);
                AddAnchor(root, "Anchor_Center",
                    new Vector3(spec.GridSize.x * CellMetres * 0.5f, 0f, spec.GridSize.y * CellMetres * 0.5f),
                    AnchorPlacement.Center, SocketDirection.North,
                    new Vector2(centerFootprint, centerFootprint), rotateYDeg: 0f);
            }
        }

        private static void AddAnchor(GameObject root, string name, Vector3 localPos,
            AnchorPlacement placement, SocketDirection wall, Vector2 footprintXZ, float rotateYDeg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = new Vector3(0f, rotateYDeg, 0f);
            var anchor = go.AddComponent<FurnitureAnchor>();
            anchor.Configure(placement, wall, footprintXZ);
        }

        // Floor or ceiling slab. If hasHole, leaves an opening sized for one grid cell
        // (CellMetres-1m from the south wall, StairHoleD deep). The hole sits in the
        // EAST cell of the staircase footprint (x offset = CellMetres) so the doors —
        // which always land in the SW-most cell of each wall — don't end up on top of
        // the staircase.
        private const float StairHoleW = 2f;
        private const float StairHoleD = 4f;
        private const float StairHoleXOffset = CellMetres; // shift hole east by one staircase cell

        private static void BuildFloorOrCeiling(GameObject root, string name, float y,
            float w, float d, float wall, bool hasHole)
        {
            if (!hasHole)
            {
                AddBox(root, name, new Vector3(w * 0.5f, y, d * 0.5f), new Vector3(w, wall, d));
                return;
            }

            float holeW       = Mathf.Min(StairHoleW, w);
            float holeMinX    = Mathf.Min(StairHoleXOffset, w - holeW);    // east of cell 0
            float holeMaxX    = holeMinX + holeW;
            float holeNorthZ  = Mathf.Min(StaircaseCells * CellMetres - 1f, d); // top step's north edge
            float holeSouthZ  = Mathf.Max(0f, holeNorthZ - StairHoleD);

            // West strip — solid floor from the west wall to the hole.
            if (holeMinX > 0.001f)
                AddBox(root, $"{name}_W",
                    new Vector3(holeMinX * 0.5f, y, d * 0.5f),
                    new Vector3(holeMinX, wall, d));
            // East strip — solid floor from the hole to the east wall.
            if (w - holeMaxX > 0.001f)
                AddBox(root, $"{name}_E",
                    new Vector3(holeMaxX + (w - holeMaxX) * 0.5f, y, d * 0.5f),
                    new Vector3(w - holeMaxX, wall, d));
            // South patch — solid floor south of the hole, inside the hole's x band.
            if (holeSouthZ > 0.001f)
                AddBox(root, $"{name}_S",
                    new Vector3(holeMinX + holeW * 0.5f, y, holeSouthZ * 0.5f),
                    new Vector3(holeW, wall, holeSouthZ));
            // North patch — solid floor between the top of the stairs and the north wall.
            if (d - holeNorthZ > 0.001f)
                AddBox(root, $"{name}_N",
                    new Vector3(holeMinX + holeW * 0.5f, y, holeNorthZ + (d - holeNorthZ) * 0.5f),
                    new Vector3(holeW, wall, d - holeNorthZ));
        }

        // Stairs from SW floor up to the NW ceiling hole. Each step is small enough that
        // a CharacterController's stepOffset walks the player up automatically — far more
        // reliable than a tilted slab, which physics-based controllers tend to slip off.
        // Wraps the steps in a parent named "Ramp" so the bootstrapper can still find
        // and remove the staircase if the Up socket ends up unconnected.
        private static void BuildRamp(GameObject root, float w, float d, float h, float wall)
        {
            const int   stepCount  = 16;
            const float rampWidth  = 2f;
            const float rampX      = 1f + StairHoleXOffset; // ramp centred in the east stair cell
            const float runStart   = 1f;
            // Clamp the ramp to the staircase footprint so multi-cell mirror rooms (e.g. the
            // basement) don't get an absurdly long ramp spanning the whole room.
            float       runEnd     = Mathf.Min(d - 1f, StaircaseCells * CellMetres - 1f);
            float       run        = runEnd - runStart;
            float       stepDepth  = run / stepCount;
            float       stepHeight = h   / stepCount;

            var ramp = new GameObject("Ramp");
            ramp.transform.SetParent(root.transform, worldPositionStays: false);

            for (int i = 0; i < stepCount; i++)
            {
                // Each step is a solid block whose top sits at (i+1) * stepHeight.
                float topY    = (i + 1) * stepHeight;
                float zCenter = runStart + i * stepDepth + stepDepth * 0.5f;

                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"Step_{i}";
                step.transform.SetParent(ramp.transform);
                step.transform.localPosition = new Vector3(rampX, topY * 0.5f, zCenter);
                step.transform.localScale    = new Vector3(rampWidth, topY, stepDepth);
            }
        }

        private static void AddBox(GameObject parent, string n, Vector3 localPos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = n;
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = localPos;
            go.transform.localScale    = scale;
        }

        // For each wall direction we either build a solid wall (no socket on this side) or a
        // wall with a single door opening at the SW-most cell of that wall — the door cell.
        // The remaining cells of a multi-cell wall stay solid. This matches DoorTransform's
        // grid-aligned door positions so doors and door frames always line up.
        private static void BuildPerimeterWall(GameObject root, SocketDirection s, HashSet<SocketDirection> sockets,
            float w, float d, float h, float wall, float c)
        {
            bool ns = s == SocketDirection.North || s == SocketDirection.South;

            if (!sockets.Contains(s))
            {
                if (ns)
                {
                    float wallZ = s == SocketDirection.North ? d : 0f;
                    AddBox(root, $"Wall{s}", new Vector3(w * 0.5f, h * 0.5f, wallZ), new Vector3(w, h, wall));
                }
                else
                {
                    float wallX = s == SocketDirection.East ? w : 0f;
                    AddBox(root, $"Wall{s}", new Vector3(wallX, h * 0.5f, d * 0.5f), new Vector3(wall, h, d));
                }
                return;
            }

            // Has a socket — solid wall covers everything past the first cell, then a
            // door frame at the first cell.
            if (ns)
            {
                float wallZ = s == SocketDirection.North ? d : 0f;
                if (w > c + 0.001f)
                {
                    float restW = w - c;
                    AddBox(root, $"Wall{s}_Rest",
                        new Vector3(c + restW * 0.5f, h * 0.5f, wallZ),
                        new Vector3(restW, h, wall));
                }
                AddDoorFrameAtCell(root, s, c, wallZ, h, wall, ns: true);
            }
            else
            {
                float wallX = s == SocketDirection.East ? w : 0f;
                if (d > c + 0.001f)
                {
                    float restD = d - c;
                    AddBox(root, $"Wall{s}_Rest",
                        new Vector3(wallX, h * 0.5f, c + restD * 0.5f),
                        new Vector3(wall, h, restD));
                }
                AddDoorFrameAtCell(root, s, c, wallX, h, wall, ns: false);
            }
        }

        // Builds the side pillars + lintel for a single door cell (centred on the cell).
        private static void AddDoorFrameAtCell(GameObject root, SocketDirection s,
            float cellSize, float wallPos, float h, float wall, bool ns)
        {
            const float dw = 1.7f; // door opening width
            const float dh = 3f;   // door opening height
            float sideW = (cellSize - dw) * 0.5f;

            if (ns)
            {
                float wallZ = wallPos;
                AddBox(root, $"Frame_{s}_L", new Vector3(sideW * 0.5f,             h * 0.5f, wallZ), new Vector3(sideW, h, wall));
                AddBox(root, $"Frame_{s}_R", new Vector3(cellSize - sideW * 0.5f,  h * 0.5f, wallZ), new Vector3(sideW, h, wall));
                AddBox(root, $"Frame_{s}_T", new Vector3(cellSize * 0.5f, dh + (h - dh) * 0.5f, wallZ), new Vector3(dw, h - dh, wall));
            }
            else
            {
                float wallX = wallPos;
                AddBox(root, $"Frame_{s}_L", new Vector3(wallX, h * 0.5f, sideW * 0.5f),                new Vector3(wall, h, sideW));
                AddBox(root, $"Frame_{s}_R", new Vector3(wallX, h * 0.5f, cellSize - sideW * 0.5f),     new Vector3(wall, h, sideW));
                AddBox(root, $"Frame_{s}_T", new Vector3(wallX, dh + (h - dh) * 0.5f, cellSize * 0.5f), new Vector3(wall, h - dh, dw));
            }
        }

        // ── Door prefab ────────────────────────────────────────────────────────

        // Must stay in sync with InteriorLayoutGenerator.DoorWidth and the door-opening
        // width in BuildPerimeterWall / PatchUnconnectedSockets.
        private const float DoorPrefabWidth  = 1.7f;
        private const float DoorPrefabHeight = 3f;

        private static GameObject RepairDoorPrefab()
        {
            var prefabPath = $"{InteriorPrefabFolder}/InteriorDoor.prefab";
            // Always overwrite — door geometry changes as we iterate, and SaveAsPrefabAsset
            // preserves the asset GUID so NetworkPrefabsList references survive.

            var root = new GameObject("InteriorDoor");
            root.AddComponent<NetworkObject>();
            var door = root.AddComponent<InteriorDoor>();

            var pivot = new GameObject("DoorPivot");
            pivot.transform.SetParent(root.transform);

            float halfW = DoorPrefabWidth * 0.5f;
            float halfH = DoorPrefabHeight * 0.5f;

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "DoorMesh";
            mesh.transform.SetParent(pivot.transform);
            mesh.transform.localPosition = new Vector3(halfW, halfH, 0);
            mesh.transform.localScale    = new Vector3(DoorPrefabWidth, DoorPrefabHeight, 0.1f);

            var col = root.AddComponent<BoxCollider>();
            col.size   = new Vector3(DoorPrefabWidth, DoorPrefabHeight, 0.1f);
            col.center = new Vector3(halfW, halfH, 0);

            var so = new SerializedObject(door);
            so.FindProperty("doorCollider").objectReferenceValue = col;
            so.FindProperty("doorPivot").objectReferenceValue    = pivot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ── Building definitions ───────────────────────────────────────────────

        private static void RepairBuildingDefinitions(RoomDefinition[] roomDefs)
        {
            var buildingSpecs = GetBuildingSpecs();
            foreach (var spec in buildingSpecs)
                RepairBuildingDefinition(spec, roomDefs);
        }

        private static BuildingDefinition RepairBuildingDefinition(BuildingSpec spec, RoomDefinition[] allDefs)
        {
            var assetPath = $"{InteriorBuildingFolder}/{spec.Name}.asset";
            var def       = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(assetPath);
            bool isNew    = def == null;
            if (isNew)
                def = ScriptableObject.CreateInstance<BuildingDefinition>();

            var so  = new SerializedObject(def);
            so.FindProperty("displayName").stringValue      = spec.DisplayName;
            so.FindProperty("minRooms").intValue            = spec.MinRooms;
            so.FindProperty("maxRooms").intValue            = spec.MaxRooms;
            so.FindProperty("minFloors").intValue           = spec.MinFloors;
            so.FindProperty("maxFloors").intValue           = spec.MaxFloors;
            so.FindProperty("floorHeightMeters").floatValue = 4f;
            so.FindProperty("gridCellMeters").floatValue    = CellMetres;
            so.FindProperty("minSpecialRooms").intValue     = spec.MinSpecial;
            so.FindProperty("maxSpecialRooms").intValue     = spec.MaxSpecial;
            so.FindProperty("themeColor").colorValue        = spec.ThemeColor;

            // Required rooms (recipe). Looked up by name from the room-def array.
            var requiredProp = so.FindProperty("requiredRooms");
            requiredProp.arraySize = spec.RequiredRooms.Length;
            for (int i = 0; i < spec.RequiredRooms.Length; i++)
            {
                var reqSpec = spec.RequiredRooms[i];
                var room = FindRoomDef(allDefs, reqSpec.RoomName);
                if (room == null)
                    Debug.LogWarning($"[Friend Slop] Building '{spec.Name}' requires '{reqSpec.RoomName}' but no such RoomDefinition exists.");
                var elem = requiredProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("definition").objectReferenceValue = room;
                elem.FindPropertyRelative("count").intValue                  = Mathf.Max(1, reqSpec.Count);

                // adjacentToAny is an array — only allocate when the recipe asked for it.
                var adjProp = elem.FindPropertyRelative("adjacentToAny");
                var adjNames = reqSpec.AdjacentToAny ?? System.Array.Empty<string>();
                adjProp.arraySize = adjNames.Length;
                for (int j = 0; j < adjNames.Length; j++)
                {
                    var adjRoom = FindRoomDef(allDefs, adjNames[j]);
                    if (adjRoom == null)
                        Debug.LogWarning($"[Friend Slop] Building '{spec.Name}' adjacency target '{adjNames[j]}' (for '{reqSpec.RoomName}') is not a known RoomDefinition.");
                    adjProp.GetArrayElementAtIndex(j).objectReferenceValue = adjRoom;
                }
            }

            // Vertical-link specialisation — only meaningful for buildings that author it.
            so.FindProperty("downwardConnectorMirror").objectReferenceValue =
                string.IsNullOrEmpty(spec.DownwardConnectorMirrorName)
                    ? null
                    : FindRoomDef(allDefs, spec.DownwardConnectorMirrorName);

            var parentsProp = so.FindProperty("downConnectorParents");
            var parentNames = spec.DownConnectorParentNames ?? System.Array.Empty<string>();
            parentsProp.arraySize = parentNames.Length;
            for (int i = 0; i < parentNames.Length; i++)
            {
                var parent = FindRoomDef(allDefs, parentNames[i]);
                if (parent == null)
                    Debug.LogWarning($"[Friend Slop] Building '{spec.Name}' downConnectorParent '{parentNames[i]}' is not a known RoomDefinition.");
                parentsProp.GetArrayElementAtIndex(i).objectReferenceValue = parent;
            }

            so.FindProperty("skipBasementExpansion").boolValue = spec.SkipBasementExpansion;
            so.FindProperty("compactLayout").boolValue         = spec.CompactLayout;
            so.FindProperty("doorsOnlyForPrivateRooms").boolValue = spec.DoorsOnlyForPrivateRooms;
            so.FindProperty("entryAtSouthernEdge").boolValue      = spec.EntryAtSouthernEdge;
            so.FindProperty("restrictUpperFloorOverhang").boolValue = spec.RestrictUpperFloorOverhang;
            so.FindProperty("forceRectangularLayout").boolValue     = spec.ForceRectangularLayout;

            // Optional pool. If the spec didn't specify, fall back to every room (legacy behaviour).
            var optionalProp = so.FindProperty("optionalPool");
            var optionalRooms = ResolveOptionalRooms(spec, allDefs);
            optionalProp.arraySize = optionalRooms.Count;
            for (int i = 0; i < optionalRooms.Count; i++)
                optionalProp.GetArrayElementAtIndex(i).objectReferenceValue = optionalRooms[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            if (isNew) AssetDatabase.CreateAsset(def, assetPath);
            EditorUtility.SetDirty(def);
            return def;
        }

        private static RoomDefinition FindRoomDef(RoomDefinition[] all, string name)
        {
            foreach (var d in all)
                if (d != null && d.name == name) return d;
            return null;
        }

        private static List<RoomDefinition> ResolveOptionalRooms(BuildingSpec spec, RoomDefinition[] allDefs)
        {
            // Null = legacy "all rooms" pool. Empty = no optional pool.
            if (spec.OptionalRoomNames == null)
                return new List<RoomDefinition>(allDefs);

            var resolved = new List<RoomDefinition>(spec.OptionalRoomNames.Length);
            foreach (var name in spec.OptionalRoomNames)
            {
                var room = FindRoomDef(allDefs, name);
                if (room == null)
                    Debug.LogWarning($"[Friend Slop] Building '{spec.Name}' references optional room '{name}' but no such RoomDefinition exists.");
                else if (!resolved.Contains(room))
                    resolved.Add(room);
            }
            return resolved;
        }

        // ── Interior catalog ───────────────────────────────────────────────────

        private static void RepairInteriorCatalog(FurnitureDefinition[] furnitureDefs = null)
        {
            var catalogPath = $"{InteriorAssetFolder}/InteriorCatalog.asset";
            // Always rebuild — adding a new building/furniture spec must update the catalog.

            var guids = AssetDatabase.FindAssets("t:BuildingDefinition", new[] { InteriorBuildingFolder });
            var defs  = new List<BuildingDefinition>();
            foreach (var guid in guids)
                defs.Add(AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid)));

            // Fall back to scanning the project if the caller didn't pass furniture defs.
            if (furnitureDefs == null)
            {
                var fGuids = AssetDatabase.FindAssets("t:FurnitureDefinition", new[] { FurnitureAssetFolder });
                var fList  = new List<FurnitureDefinition>(fGuids.Length);
                foreach (var g in fGuids)
                    fList.Add(AssetDatabase.LoadAssetAtPath<FurnitureDefinition>(AssetDatabase.GUIDToAssetPath(g)));
                furnitureDefs = fList.ToArray();
            }

            var catalog   = AssetDatabase.LoadAssetAtPath<InteriorCatalog>(catalogPath);
            bool isNewCat = catalog == null;
            if (isNewCat)
                catalog = ScriptableObject.CreateInstance<InteriorCatalog>();

            var so      = new SerializedObject(catalog);
            var arr     = so.FindProperty("buildings");
            arr.arraySize = defs.Count;
            for (int i = 0; i < defs.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];

            var furnArr = so.FindProperty("furniture");
            furnArr.arraySize = furnitureDefs.Length;
            for (int i = 0; i < furnitureDefs.Length; i++)
                furnArr.GetArrayElementAtIndex(i).objectReferenceValue = furnitureDefs[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            if (isNewCat) AssetDatabase.CreateAsset(catalog, catalogPath);
            EditorUtility.SetDirty(catalog);
        }

        // ── NetworkPrefabsList ─────────────────────────────────────────────────

        private static void RepairDoorInNetworkPrefabsList(GameObject doorPrefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null || doorPrefab == null) return;

            var so       = new SerializedObject(list);
            var listProp = so.FindProperty("List");
            if (listProp == null) return;

            // Idempotent: bail if the door is already registered.
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var entry = listProp.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Prefab").objectReferenceValue == doorPrefab) return;
            }

            var idx = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(idx);
            var newEntry = listProp.GetArrayElementAtIndex(idx);
            newEntry.FindPropertyRelative("Override").enumValueIndex                    = (int)NetworkPrefabOverride.None;
            newEntry.FindPropertyRelative("Prefab").objectReferenceValue                = doorPrefab;
            newEntry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
            newEntry.FindPropertyRelative("SourceHashToOverride").uintValue             = 0;
            newEntry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(list);
        }

        // ── Loading screen canvas ──────────────────────────────────────────────

        private static GameObject BuildLoadingScreenCanvas(Transform parent)
        {
            var canvasGo = new GameObject("InteriorLoadingCanvas");
            canvasGo.transform.SetParent(parent);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasGroup>();
            canvasGo.AddComponent<InteriorLoadingScreen>();

            var bg = new GameObject("Background");
            bg.transform.SetParent(canvasGo.transform, false);
            var img = bg.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.85f);
            var rt = bg.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            return canvasGo;
        }

        // ── Folder creation ────────────────────────────────────────────────────

        private static void EnsureInteriorFolders()
        {
            foreach (var path in new[]
            {
                InteriorPrefabFolder, InteriorRoomFolder,
                InteriorAssetFolder, InteriorRoomDefFolder, InteriorBuildingFolder
            })
            {
                if (!AssetDatabase.IsValidFolder(path))
                {
                    var parts = path.Split('/');
                    var parent = string.Join("/", parts, 0, parts.Length - 1);
                    AssetDatabase.CreateFolder(parent, parts[parts.Length - 1]);
                }
            }
        }

        // ── Spec data ──────────────────────────────────────────────────────────

        private static readonly SocketDirection[] AllHorizontalSockets =
            { SocketDirection.North, SocketDirection.South, SocketDirection.East, SocketDirection.West };

        private static RoomSpec[] GetRoomSpecs() => new[]
        {
            // Legacy generic rooms (kept so the existing Small/Medium/Large/Multifloor buildings still work).
            new RoomSpec("Room_Entry_1x1",   new Vector2Int(2,2), RoomCategory.Entry,   AllHorizontalSockets, false, 10),
            new RoomSpec("Room_Generic_1x1", new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 20),
            new RoomSpec("Room_Generic_1x2", new Vector2Int(2,4), RoomCategory.Generic, AllHorizontalSockets, false, 15),
            new RoomSpec("Room_Generic_2x2", new Vector2Int(4,4), RoomCategory.Generic, AllHorizontalSockets, false, 10),
            new RoomSpec("Room_Utility_1x1", new Vector2Int(2,2), RoomCategory.Utility, new[]{SocketDirection.South}, false, 10),
            new RoomSpec("Room_Special_2x2", new Vector2Int(4,4), RoomCategory.Special, AllHorizontalSockets, false, 5),
            new RoomSpec("Room_Stair_1x1",   new Vector2Int(2,2), RoomCategory.Generic, new[]{SocketDirection.North,SocketDirection.South,SocketDirection.Up,SocketDirection.Down}, true, 10, kind: RoomKind.Stair),

            // Residential type rooms.
            new RoomSpec("Room_Residential_Entry_2x2",       new Vector2Int(2,2), RoomCategory.Entry,   AllHorizontalSockets, false, 10,
                kind: RoomKind.Entry,
                furnitureTags: new[]{ FurnitureTags.Shared }),
            new RoomSpec("Room_Residential_Kitchen_2x3",     new Vector2Int(2,3), RoomCategory.Generic, AllHorizontalSockets, false, 10,
                kind: RoomKind.Kitchen,
                furnitureTags: new[]{ FurnitureTags.Kitchen, FurnitureTags.Shared },
                furnitureRules: new[]
                {
                    Rule("stove",   min: 1, max: 1),
                    Rule("fridge",  min: 1, max: 1),
                    Rule("sink",    min: 0, max: 1),
                    Rule("counter", min: 0, max: 2),
                }),
            new RoomSpec("Room_Residential_LivingRoom_3x3",  new Vector2Int(3,3), RoomCategory.Special, AllHorizontalSockets, false, 5,
                kind: RoomKind.LivingRoom,
                entryCandidate: true,
                furnitureTags: new[]{ FurnitureTags.LivingRoom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 6)),
            new RoomSpec("Room_Residential_Hallway_4x1",     new Vector2Int(4,1), RoomCategory.Generic, AllHorizontalSockets, false, 8,
                kind: RoomKind.Hallway,
                furnitureTags: new[]{ FurnitureTags.Hallway, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(1, 2)),
            new RoomSpec("Room_Residential_Bedroom_2x2",     new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 15,
                kind: RoomKind.Bedroom,
                furnitureTags: new[]{ FurnitureTags.Bedroom, FurnitureTags.Shared },
                furnitureRules: new[]
                {
                    Rule("bed",         min: 1, max: 1),
                    Rule("nightstand",  min: 0, max: 2),
                    Rule("dresser",     min: 0, max: 1),
                }),
            new RoomSpec("Room_Residential_Bathroom_2x2",    new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 10,
                kind: RoomKind.Bathroom,
                floorRestriction: FloorRestriction.TopFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Bathroom, FurnitureTags.Utility },
                furnitureRules: new[]
                {
                    Rule("toilet",  min: 1, max: 1),
                    Rule("sink",    min: 1, max: 1),
                    Rule("bathtub", min: 0, max: 1),
                },
                maxHorizontalConnections: 1),
            new RoomSpec("Room_Residential_DiningRoom_2x1",  new Vector2Int(2,1), RoomCategory.Special, AllHorizontalSockets, false, 5,
                kind: RoomKind.DiningRoom,
                furnitureTags: new[]{ FurnitureTags.Dining, FurnitureTags.Shared }),
            new RoomSpec("Room_Residential_Basement_4x4",    new Vector2Int(4,4), RoomCategory.Special,
                new[]{SocketDirection.North,SocketDirection.South,SocketDirection.East,SocketDirection.West,SocketDirection.Up}, false, 5,
                kind: RoomKind.Basement,
                furnitureTags: new[]{ FurnitureTags.Basement, FurnitureTags.Storage, FurnitureTags.Shared }),

            // Residential — small utility rooms.
            new RoomSpec("Room_Residential_Laundry_2x2",     new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.Laundry,
                furnitureTags: new[]{ FurnitureTags.Laundry, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(2, 4),
                furnitureRules: new[]
                {
                    Rule("washer", min: 1, max: 1),
                    Rule("dryer",  min: 1, max: 1),
                }),
            new RoomSpec("Room_Residential_Pantry_2x1",      new Vector2Int(2,1), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.Pantry,
                furnitureTags: new[]{ FurnitureTags.Storage, FurnitureTags.Kitchen, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(1, 3)),
            new RoomSpec("Room_Residential_WalkinCloset_1x1", new Vector2Int(1,1), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.WalkinCloset,
                furnitureTags: new[]{ FurnitureTags.Closet, FurnitureTags.Bedroom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(1, 3),
                furnitureRules: new[] { Rule("dresser", min: 0, max: 2) },
                maxHorizontalConnections: 1),
            new RoomSpec("Room_Residential_PowderRoom_1x1",  new Vector2Int(1,1), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.PowderRoom,
                floorRestriction: FloorRestriction.EntryFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Bathroom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(2, 3),
                furnitureRules: new[]
                {
                    Rule("toilet", min: 1, max: 1),
                    Rule("sink",   min: 1, max: 1),
                },
                maxHorizontalConnections: 1),
            new RoomSpec("Room_Residential_MudRoom_1x1",     new Vector2Int(1,1), RoomCategory.Utility, AllHorizontalSockets, false, 3,
                kind: RoomKind.MudRoom,
                furnitureTags: new[]{ FurnitureTags.MudRoom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(2, 3)),
            new RoomSpec("Room_Residential_LinenCloset_1x1", new Vector2Int(1,1), RoomCategory.Utility, AllHorizontalSockets, false, 3,
                kind: RoomKind.LinenCloset,
                furnitureTags: new[]{ FurnitureTags.Closet, FurnitureTags.Storage, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(1, 2),
                maxHorizontalConnections: 1),

            // Residential — specialty living spaces.
            new RoomSpec("Room_Residential_MasterBedroom_4x2", new Vector2Int(4,2), RoomCategory.Generic, AllHorizontalSockets, false, 6,
                kind: RoomKind.MasterBedroom,
                furnitureTags: new[]{ FurnitureTags.Bedroom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5),
                furnitureRules: new[]
                {
                    Rule("bed",         min: 1, max: 1),
                    Rule("nightstand",  min: 0, max: 2),
                    Rule("dresser",     min: 0, max: 2),
                }),
            new RoomSpec("Room_Residential_MasterBathroom_2x2", new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.MasterBathroom,
                floorRestriction: FloorRestriction.TopFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Bathroom, FurnitureTags.Shared },
                furnitureRules: new[]
                {
                    Rule("toilet",  min: 1, max: 1),
                    Rule("sink",    min: 1, max: 1),
                    Rule("bathtub", min: 0, max: 1),
                },
                maxHorizontalConnections: 1),
            new RoomSpec("Room_Residential_Office_2x2",      new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 5,
                kind: RoomKind.Office,
                floorRestriction: FloorRestriction.TopFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Office, FurnitureTags.Shared },
                furnitureRules: new[]
                {
                    Rule("desk",      min: 1, max: 1),
                    Rule("bookshelf", min: 0, max: 1),
                }),
            new RoomSpec("Room_Residential_Den_4x2",         new Vector2Int(4,2), RoomCategory.Special, AllHorizontalSockets, false, 5,
                kind: RoomKind.Den,
                furnitureTags: new[]{ FurnitureTags.LivingRoom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5)),
            new RoomSpec("Room_Residential_SunRoom_2x4",     new Vector2Int(2,4), RoomCategory.Special, AllHorizontalSockets, false, 4,
                kind: RoomKind.SunRoom,
                furnitureTags: new[]{ FurnitureTags.LivingRoom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(2, 4)),
            new RoomSpec("Room_Residential_Library_2x2",     new Vector2Int(2,2), RoomCategory.Special, AllHorizontalSockets, false, 4,
                kind: RoomKind.Library,
                furnitureTags: new[]{ FurnitureTags.Office, FurnitureTags.LivingRoom, FurnitureTags.Shared },
                furnitureRules: new[] { Rule("bookshelf", min: 2, max: 4) }),

            // Residential — garage.
            new RoomSpec("Room_Residential_Garage_3x3",      new Vector2Int(3,3), RoomCategory.Special, AllHorizontalSockets, false, 5,
                kind: RoomKind.Garage,
                maxCount: 1,
                furnitureTags: new[]{ FurnitureTags.Garage, FurnitureTags.Workshop, FurnitureTags.Storage, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5),
                furnitureRules: new[]
                {
                    Rule("car",       min: 0, max: 1),
                    Rule("workbench", min: 0, max: 2),
                    Rule("locker",    min: 0, max: 2),
                }),

            // Residential — basement-only extensions (FloorRestriction keeps them in the basement).
            new RoomSpec("Room_Residential_GameRoom_2x4",    new Vector2Int(2,4), RoomCategory.Special, AllHorizontalSockets, false, 4,
                kind: RoomKind.GameRoom,
                floorRestriction: FloorRestriction.BottomFloorOnly,
                furnitureTags: new[]{ FurnitureTags.GameRoom, FurnitureTags.LivingRoom, FurnitureTags.BreakRoom, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5)),
            new RoomSpec("Room_Residential_WineCellar_2x2",  new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.WineCellar,
                floorRestriction: FloorRestriction.BottomFloorOnly,
                furnitureTags: new[]{ FurnitureTags.WineCellar, FurnitureTags.Storage, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5),
                furnitureRules: new[] { Rule("wine_rack", min: 1, max: 3) }),
            new RoomSpec("Room_Residential_Workshop_2x2",    new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 4,
                kind: RoomKind.Workshop,
                floorRestriction: FloorRestriction.BottomFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Workshop, FurnitureTags.Shared },
                furnitureRules: new[] { Rule("workbench", min: 1, max: 2) }),
            new RoomSpec("Room_Residential_MechanicalRoom_2x2", new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 3,
                kind: RoomKind.MechanicalRoom,
                floorRestriction: FloorRestriction.BottomFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Mechanical, FurnitureTags.Storage, FurnitureTags.Power, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(2, 4),
                furnitureRules: new[]
                {
                    Rule("furnace",      min: 1, max: 1),
                    Rule("water_heater", min: 1, max: 1),
                }),

            // Office type rooms.
            new RoomSpec("Room_Office_Lobby_2x2",            new Vector2Int(4,4), RoomCategory.Entry,   AllHorizontalSockets, false, 10,
                furnitureTags: new[]{ FurnitureTags.Lobby, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5)),
            new RoomSpec("Room_Office_Reception_1x2",        new Vector2Int(2,4), RoomCategory.Special, AllHorizontalSockets, false, 5,
                entryCandidate: true,
                furnitureTags: new[]{ FurnitureTags.Lobby, FurnitureTags.Shared }),
            new RoomSpec("Room_Office_Hallway_1x2",          new Vector2Int(2,4), RoomCategory.Generic, AllHorizontalSockets, false, 14,
                furnitureTags: new[]{ FurnitureTags.Hallway, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(0, 1)),
            new RoomSpec("Room_Office_Cubicle_1x1",          new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 15,
                furnitureTags: new[]{ FurnitureTags.Cubicle, FurnitureTags.Office, FurnitureTags.Shared },
                furnitureRules: new[]
                {
                    Rule("desk",  min: 1, max: 1),
                    Rule("chair", min: 0, max: 1),
                }),
            new RoomSpec("Room_Office_OpenPlan_2x2",         new Vector2Int(4,4), RoomCategory.Special, AllHorizontalSockets, false, 6,
                furnitureTags: new[]{ FurnitureTags.Cubicle, FurnitureTags.Office, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(4, 7)),
            new RoomSpec("Room_Office_Conference_1x2",       new Vector2Int(2,4), RoomCategory.Special, AllHorizontalSockets, false, 5,
                furnitureTags: new[]{ FurnitureTags.Conference, FurnitureTags.Shared }),
            new RoomSpec("Room_Office_ConferenceLarge_2x2",  new Vector2Int(4,4), RoomCategory.Special, AllHorizontalSockets, false, 4,
                furnitureTags: new[]{ FurnitureTags.Conference, FurnitureTags.Shared },
                furnitureCountRange: new Vector2Int(3, 5)),
            new RoomSpec("Room_Office_ManagerOffice_1x1",    new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 8,
                furnitureTags: new[]{ FurnitureTags.Office, FurnitureTags.Shared },
                furnitureRules: new[]
                {
                    Rule("desk",       min: 1, max: 1),
                    Rule("bookshelf",  min: 0, max: 1),
                }),
            new RoomSpec("Room_Office_ServerRoom_1x1",       new Vector2Int(2,2), RoomCategory.Special, AllHorizontalSockets, false, 3,
                floorRestriction: FloorRestriction.TopFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Server, FurnitureTags.Utility }),
            new RoomSpec("Room_Office_BreakRoom_1x2",        new Vector2Int(2,4), RoomCategory.Generic, AllHorizontalSockets, false, 6,
                furnitureTags: new[]{ FurnitureTags.BreakRoom, FurnitureTags.Shared }),
            new RoomSpec("Room_Office_Bathroom_1x1",         new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 10,
                furnitureTags: new[]{ FurnitureTags.Bathroom, FurnitureTags.Utility },
                furnitureRules: new[]
                {
                    Rule("toilet", min: 1, max: 2),
                    Rule("sink",   min: 1, max: 2),
                }),
            new RoomSpec("Room_Office_Storage_1x1",          new Vector2Int(2,2), RoomCategory.Utility, new[]{SocketDirection.South}, false, 5,
                furnitureTags: new[]{ FurnitureTags.Storage, FurnitureTags.Utility }),

            // Factory type rooms.
            new RoomSpec("Room_Factory_LoadingBay_2x2",       new Vector2Int(4,4), RoomCategory.Entry,   AllHorizontalSockets, false, 10,
                furnitureTags: new[]{ FurnitureTags.LoadingBay, FurnitureTags.Factory },
                furnitureCountRange: new Vector2Int(2, 4)),
            new RoomSpec("Room_Factory_OfficeReception_1x1",  new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 6,
                entryCandidate: true,
                furnitureTags: new[]{ FurnitureTags.Office, FurnitureTags.Lobby, FurnitureTags.Shared }),
            new RoomSpec("Room_Factory_Catwalk_1x2",          new Vector2Int(2,4), RoomCategory.Generic, AllHorizontalSockets, false, 14,
                furnitureTags: new[]{ FurnitureTags.Hallway, FurnitureTags.Factory },
                furnitureCountRange: new Vector2Int(0, 1)),
            new RoomSpec("Room_Factory_Workshop_2x2",         new Vector2Int(4,4), RoomCategory.Special, AllHorizontalSockets, false, 6,
                furnitureTags: new[]{ FurnitureTags.Workshop, FurnitureTags.Factory },
                furnitureCountRange: new Vector2Int(3, 6),
                furnitureRules: new[]
                {
                    Rule("workbench", min: 1, max: 3),
                }),
            new RoomSpec("Room_Factory_AssemblyLine_2x2",     new Vector2Int(4,4), RoomCategory.Special, AllHorizontalSockets, false, 5,
                furnitureTags: new[]{ FurnitureTags.Workshop, FurnitureTags.Factory },
                furnitureCountRange: new Vector2Int(3, 6),
                furnitureRules: new[]
                {
                    Rule("workbench", min: 2, max: 4),
                }),
            new RoomSpec("Room_Factory_Storage_1x2",          new Vector2Int(2,4), RoomCategory.Generic, AllHorizontalSockets, false, 8,
                furnitureTags: new[]{ FurnitureTags.Storage, FurnitureTags.Factory }),
            new RoomSpec("Room_Factory_HazardStorage_1x1",    new Vector2Int(2,2), RoomCategory.Special, AllHorizontalSockets, false, 3,
                furnitureTags: new[]{ FurnitureTags.Storage, FurnitureTags.Factory }),
            new RoomSpec("Room_Factory_ManagerOffice_1x1",    new Vector2Int(2,2), RoomCategory.Generic, AllHorizontalSockets, false, 6,
                furnitureTags: new[]{ FurnitureTags.Office, FurnitureTags.Shared }),
            new RoomSpec("Room_Factory_ForemanOffice_1x1",    new Vector2Int(2,2), RoomCategory.Special, AllHorizontalSockets, false, 3,
                floorRestriction: FloorRestriction.TopFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Office, FurnitureTags.Shared }),
            new RoomSpec("Room_Factory_ControlRoom_1x1",      new Vector2Int(2,2), RoomCategory.Special, AllHorizontalSockets, false, 3,
                floorRestriction: FloorRestriction.TopFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Control, FurnitureTags.Office, FurnitureTags.Factory }),
            new RoomSpec("Room_Factory_PowerRoom_1x1",        new Vector2Int(2,2), RoomCategory.Special, AllHorizontalSockets, false, 4,
                floorRestriction: FloorRestriction.BottomFloorOnly,
                furnitureTags: new[]{ FurnitureTags.Power, FurnitureTags.Utility, FurnitureTags.Factory }),
            new RoomSpec("Room_Factory_Cafeteria_1x2",        new Vector2Int(2,4), RoomCategory.Generic, AllHorizontalSockets, false, 6,
                furnitureTags: new[]{ FurnitureTags.Cafeteria, FurnitureTags.BreakRoom, FurnitureTags.Shared }),
            new RoomSpec("Room_Factory_LockerRoom_1x1",       new Vector2Int(2,2), RoomCategory.Utility, new[]{SocketDirection.South}, false, 5,
                furnitureTags: new[]{ FurnitureTags.Locker, FurnitureTags.Factory, FurnitureTags.Utility },
                furnitureRules: new[]
                {
                    Rule("locker", min: 3, max: 6),
                }),
            new RoomSpec("Room_Factory_Bathroom_1x1",         new Vector2Int(2,2), RoomCategory.Utility, AllHorizontalSockets, false, 8,
                furnitureTags: new[]{ FurnitureTags.Bathroom, FurnitureTags.Utility },
                furnitureRules: new[]
                {
                    Rule("toilet", min: 1, max: 2),
                    Rule("sink",   min: 1, max: 2),
                }),
        };

        private static BuildingSpec[] GetBuildingSpecs() => new[]
        {
            // Legacy untyped buildings — no recipe; the full RoomPool is used.
            BuildingSpec.Legacy("Building_Small",      "Small Building",       4,  8,  1, 1, 0, 1),
            BuildingSpec.Legacy("Building_Medium",     "Medium Building",      8, 20,  2, 3, 1, 2),
            BuildingSpec.Legacy("Building_Large",      "Large Building",      20, 40,  3, 5, 2, 4),
            BuildingSpec.Legacy("Building_Multifloor", "Multi-floor Building",10, 18,  3, 3, 1, 2),

            // Typed buildings — required + optional recipes.
            new BuildingSpec("Building_Residential", "Residential",
                // 12..22 rooms across 2–3 floors (basement + ground + optional upper).
                // Bedrooms / master suite / office prefer the upper floor, so 3-floor
                // houses get the classic "ground = social, upper = private" layout.
                minR: 12, maxR: 22, minF: 2, maxF: 3, minS: 2, maxS: 4,
                themeColor: new Color(0.95f, 0.85f, 0.70f),
                // Placement order matters here. Entry is placed first (always). The
                // LivingRoom comes next via the entry-candidate path (open passage off
                // the Entry, or it IS the entry). Then ProcessAdjacencyConstraints walks
                // this list in order: Kitchen is placed adjacent to the LivingRoom BEFORE
                // any optional rooms exist, which gives the kitchen-faces-the-void rule
                // the most empty cells to work with. DiningRoom comes after Kitchen.
                required: new[]
                {
                    Req("Room_Residential_Entry_2x2",      1),
                    Req("Room_Residential_LivingRoom_3x3", 1),
                    // Kitchen must sit next to the LivingRoom. Placing it early — before
                    // bedrooms/bathrooms fill the surrounding cells — makes it much more
                    // likely that one of its 2-wide walls ends up facing the void.
                    Req("Room_Residential_Kitchen_2x3",    1, "Room_Residential_LivingRoom_3x3"),
                    Req("Room_Residential_Hallway_4x1",    1),
                    // Top-floor full bathroom (TopFloorOnly). For 2-floor houses this is
                    // the entry/top floor; for 3-floor houses this lands on the upper.
                    Req("Room_Residential_Bathroom_2x2",   1),
                    // Entry-floor powder room (EntryFloorOnly). Guarantees a half-bath on
                    // the ground floor even when there's a full bath upstairs.
                    Req("Room_Residential_PowderRoom_1x1", 1),
                },
                optionalRoomNames: new[]
                {
                    // Core repeatable rooms.
                    "Room_Residential_Bedroom_2x2",
                    "Room_Residential_Bathroom_2x2",
                    "Room_Residential_Hallway_4x1",
                    "Room_Stair_1x1",                       // vertical connector pool entry
                    // Specialty bedroom suite — placement filter pairs them.
                    "Room_Residential_MasterBedroom_4x2",
                    "Room_Residential_MasterBathroom_2x2",  // adjacency rule: only off MasterBedroom
                    "Room_Residential_WalkinCloset_1x1",    // adjacency rule: only off any bedroom
                    // Small utility rooms.
                    "Room_Residential_Laundry_2x2",
                    "Room_Residential_Pantry_2x1",          // adjacency rule: only off Kitchen
                    "Room_Residential_DiningRoom_2x1",      // adjacency rule: long side fully against Kitchen
                    "Room_Residential_MudRoom_1x1",         // adjacency rule: only off Entry
                    "Room_Residential_LinenCloset_1x1",
                    // Specialty living spaces.
                    "Room_Residential_Office_2x2",
                    "Room_Residential_Den_4x2",
                    "Room_Residential_SunRoom_2x4",         // post-validates one long wall facing the void
                    "Room_Residential_Library_2x2",
                    // Attached garage.
                    "Room_Residential_Garage_3x3",          // post-validates at least one wall facing the void
                    // Basement-only (FloorRestriction.BottomFloorOnly handles the placement).
                    "Room_Residential_GameRoom_2x4",
                    "Room_Residential_WineCellar_2x2",
                    "Room_Residential_Workshop_2x2",
                    "Room_Residential_MechanicalRoom_2x2",
                },
                // When the basement floor exists, the stair on the entry floor goes there
                // — and must be placed adjacent to one of these rooms.
                downwardConnectorMirrorName: "Room_Residential_Basement_4x4",
                downConnectorParentNames: new[]
                {
                    "Room_Residential_Kitchen_2x3",
                    "Room_Residential_LivingRoom_3x3",
                    "Room_Residential_Hallway_4x1",
                },
                skipBasementExpansion: false,    // basement can fill with GameRoom/Workshop/etc.
                compactLayout: true,
                doorsOnlyForPrivateRooms: true,
                entryAtSouthernEdge: true,
                restrictUpperFloorOverhang: true,
                forceRectangularLayout: true),

            new BuildingSpec("Building_Office", "Office",
                // Larger so we can fit per-floor hallways + cubicles + meeting rooms.
                minR: 10, maxR: 18, minF: 2, maxF: 3, minS: 2, maxS: 4,
                themeColor: new Color(0.70f, 0.80f, 0.95f),
                required: new[]
                {
                    // Either Lobby OR Reception ends up as the entry (entry-candidate logic).
                    Req("Room_Office_Lobby_2x2",         1),
                    Req("Room_Office_Reception_1x2",     1),
                    // At least one Hallway is guaranteed via required; more come via the
                    // optional pool's high-weight Hallway entry, so multi-floor offices
                    // tend to get a hallway on most floors.
                    Req("Room_Office_Hallway_1x2",       1),
                    Req("Room_Office_Conference_1x2",    1, "Room_Office_Hallway_1x2"),
                    Req("Room_Office_Bathroom_1x1",      1, "Room_Office_Hallway_1x2"),
                },
                optionalRoomNames: new[]
                {
                    "Room_Office_Cubicle_1x1",
                    "Room_Office_Hallway_1x2",
                    "Room_Office_OpenPlan_2x2",
                    "Room_Office_ConferenceLarge_2x2",
                    "Room_Office_ManagerOffice_1x1",
                    "Room_Office_BreakRoom_1x2",
                    "Room_Office_Bathroom_1x1",
                    "Room_Office_Storage_1x1",
                    "Room_Office_ServerRoom_1x1",       // top-floor-only enforced by FloorRestriction
                    "Room_Stair_1x1",                   // vertical connector
                }),

            new BuildingSpec("Building_Factory", "Factory",
                minR: 16, maxR: 28, minF: 3, maxF: 4, minS: 3, maxS: 5,
                themeColor: new Color(0.75f, 0.55f, 0.35f),
                required: new[]
                {
                    // Entry: either LoadingBay or OfficeReception (substitute entry).
                    Req("Room_Factory_LoadingBay_2x2",      1),
                    Req("Room_Factory_OfficeReception_1x1", 1),
                    // Connectivity hub for worker facilities.
                    Req("Room_Factory_Catwalk_1x2",         1),
                    // Core production floor + a manager office.
                    Req("Room_Factory_Workshop_2x2",        1),
                    Req("Room_Factory_ManagerOffice_1x1",   1),
                    // Adjacency-bound rooms — logistics flow & worker facilities.
                    Req("Room_Factory_Storage_1x2",         1, "Room_Factory_LoadingBay_2x2"),
                    Req("Room_Factory_HazardStorage_1x1",   1, "Room_Factory_LoadingBay_2x2"),
                    Req("Room_Factory_Cafeteria_1x2",       1, "Room_Factory_Catwalk_1x2"),
                    Req("Room_Factory_Bathroom_1x1",        1, "Room_Factory_Catwalk_1x2"),
                },
                optionalRoomNames: new[]
                {
                    "Room_Factory_Workshop_2x2",
                    "Room_Factory_AssemblyLine_2x2",
                    "Room_Factory_Storage_1x2",
                    "Room_Factory_Catwalk_1x2",           // extra catwalks for upper floors
                    "Room_Factory_ManagerOffice_1x1",
                    "Room_Factory_ForemanOffice_1x1",     // TopFloorOnly
                    "Room_Factory_ControlRoom_1x1",       // TopFloorOnly
                    "Room_Factory_PowerRoom_1x1",         // BottomFloorOnly
                    "Room_Factory_LockerRoom_1x1",
                    "Room_Factory_Bathroom_1x1",
                    "Room_Stair_1x1",
                }),
        };

        private readonly struct RoomSpec
        {
            public readonly string Name;
            public readonly Vector2Int GridSize;
            public readonly RoomCategory Category;
            public readonly RoomKind Kind;
            public readonly SocketDirection[] Sockets;
            public readonly bool IsVerticalConnector;
            public readonly int Weight;
            public readonly bool EntryCandidate;
            public readonly FloorRestriction FloorRestriction;
            public readonly string[] FurnitureTags;
            public readonly Vector2Int FurnitureCountRange;
            public readonly (string kind, int min, int max)[] FurnitureRules;
            public readonly int MaxHorizontalConnections;
            public readonly int MaxCount;

            public RoomSpec(string name, Vector2Int size, RoomCategory cat, SocketDirection[] sockets,
                bool vertical, int weight, bool entryCandidate = false,
                FloorRestriction floorRestriction = FloorRestriction.Any,
                string[] furnitureTags = null,
                Vector2Int furnitureCountRange = default,
                (string kind, int min, int max)[] furnitureRules = null,
                int maxHorizontalConnections = -1,
                RoomKind kind = RoomKind.Unspecified,
                int maxCount = 0)
            {
                Name = name; GridSize = size; Category = cat; Sockets = sockets;
                IsVerticalConnector = vertical; Weight = weight; EntryCandidate = entryCandidate;
                FloorRestriction = floorRestriction;
                FurnitureTags = furnitureTags ?? new[] { FriendSlop.Interiors.FurnitureTags.Shared };
                FurnitureCountRange = furnitureCountRange == default ? new Vector2Int(2, 4) : furnitureCountRange;
                FurnitureRules = furnitureRules ?? System.Array.Empty<(string, int, int)>();
                MaxHorizontalConnections = maxHorizontalConnections;
                Kind = kind;
                MaxCount = maxCount;
            }
        }

        // Shorthand for declaring furniture rules inline.
        private static (string kind, int min, int max) Rule(string kind, int min = 0, int max = 0) =>
            (kind, min, max);

        private readonly struct BuildingSpec
        {
            public readonly string Name, DisplayName;
            public readonly int MinRooms, MaxRooms, MinFloors, MaxFloors, MinSpecial, MaxSpecial;
            public readonly RequiredRoomSpec[] RequiredRooms;
            // null  → use every RoomDefinition (legacy behaviour).
            // empty → no optional rooms; only required rooms are placeable.
            public readonly string[] OptionalRoomNames;
            public readonly Color ThemeColor;
            public readonly string DownwardConnectorMirrorName;
            public readonly string[] DownConnectorParentNames;
            public readonly bool SkipBasementExpansion;
            public readonly bool CompactLayout;
            public readonly bool DoorsOnlyForPrivateRooms;
            public readonly bool EntryAtSouthernEdge;
            public readonly bool RestrictUpperFloorOverhang;
            public readonly bool ForceRectangularLayout;

            public BuildingSpec(string name, string displayName,
                int minR, int maxR, int minF, int maxF, int minS, int maxS,
                Color themeColor,
                RequiredRoomSpec[] required,
                string[] optionalRoomNames,
                string downwardConnectorMirrorName = null,
                string[] downConnectorParentNames = null,
                bool skipBasementExpansion = false,
                bool compactLayout = true,
                bool doorsOnlyForPrivateRooms = false,
                bool entryAtSouthernEdge = false,
                bool restrictUpperFloorOverhang = false,
                bool forceRectangularLayout = false)
            {
                Name = name; DisplayName = displayName;
                MinRooms = minR; MaxRooms = maxR; MinFloors = minF; MaxFloors = maxF;
                MinSpecial = minS; MaxSpecial = maxS;
                RequiredRooms = required ?? System.Array.Empty<RequiredRoomSpec>();
                OptionalRoomNames = optionalRoomNames;
                ThemeColor = themeColor;
                DownwardConnectorMirrorName = downwardConnectorMirrorName;
                DownConnectorParentNames = downConnectorParentNames;
                SkipBasementExpansion = skipBasementExpansion;
                CompactLayout = compactLayout;
                DoorsOnlyForPrivateRooms = doorsOnlyForPrivateRooms;
                EntryAtSouthernEdge = entryAtSouthernEdge;
                RestrictUpperFloorOverhang = restrictUpperFloorOverhang;
                ForceRectangularLayout = forceRectangularLayout;
            }

            public static BuildingSpec Legacy(string name, string displayName,
                int minR, int maxR, int minF, int maxF, int minS, int maxS) =>
                new BuildingSpec(name, displayName, minR, maxR, minF, maxF, minS, maxS,
                    themeColor: Color.white,
                    required: System.Array.Empty<RequiredRoomSpec>(),
                    optionalRoomNames: null);
        }

        private readonly struct RequiredRoomSpec
        {
            public readonly string RoomName;
            public readonly int Count;
            public readonly string[] AdjacentToAny;

            public RequiredRoomSpec(string roomName, int count, params string[] adjacentToAny)
            {
                RoomName = roomName; Count = count;
                AdjacentToAny = adjacentToAny;
            }
        }

        // Sugar helper for readable BuildingSpec recipes.
        private static RequiredRoomSpec Req(string roomName, int count, params string[] adjacentToAny) =>
            new RequiredRoomSpec(roomName, count, adjacentToAny);
    }
}
#endif
