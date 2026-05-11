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
            RepairInteriorCatalog();
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
                // Make sure the existing definition still points at the rebuilt prefab.
                var existingSo = new SerializedObject(existing);
                existingSo.FindProperty("prefab").objectReferenceValue = prefab;
                existingSo.ApplyModifiedPropertiesWithoutUndo();
                return existing;
            }

            var def = ScriptableObject.CreateInstance<RoomDefinition>();
            var so  = new SerializedObject(def);
            so.FindProperty("gridSize").vector2IntValue = spec.GridSize;
            so.FindProperty("category").enumValueIndex  = (int)spec.Category;
            so.FindProperty("isVerticalConnector").boolValue = spec.IsVerticalConnector;
            so.FindProperty("weight").intValue          = spec.Weight;

            var socketsArr = so.FindProperty("sockets");
            socketsArr.arraySize = spec.Sockets.Length;
            for (int i = 0; i < spec.Sockets.Length; i++)
                socketsArr.GetArrayElementAtIndex(i).enumValueIndex = (int)spec.Sockets[i];

            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(def, assetPath);
            return def;
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

        private static void BuildRoomGeometry(GameObject root, RoomSpec spec)
        {
            const float c = 8f;  // metres per grid cell
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
        }

        // Floor or ceiling slab. If hasHole, leaves an opening that begins at the top
        // step of the staircase (z ≈ 6.625 with 16 steps over 6 m run) and extends
        // SOUTH from there, giving the player head-clearance during ascent. The strip
        // between the top step and the north wall stays solid floor.
        // 2 m wide (stair width) × 3 m deep.
        private const float StairHoleW = 2f;
        private const float StairHoleD = 4f;
        private const float StairHoleNorthZ = 7f; // top step's north edge for the current ramp config

        private static void BuildFloorOrCeiling(GameObject root, string name, float y,
            float w, float d, float wall, bool hasHole)
        {
            if (!hasHole)
            {
                AddBox(root, name, new Vector3(w * 0.5f, y, d * 0.5f), new Vector3(w, wall, d));
                return;
            }

            float holeW       = Mathf.Min(StairHoleW, w);
            float holeNorthZ  = Mathf.Min(StairHoleNorthZ, d);
            float holeSouthZ  = Mathf.Max(0f, holeNorthZ - StairHoleD);

            // East strip — covers everything to the east of the hole (x=holeW..w, full Z).
            AddBox(root, $"{name}_E",
                new Vector3(holeW + (w - holeW) * 0.5f, y, d * 0.5f),
                new Vector3(w - holeW, wall, d));
            // South patch — solid floor south of the hole.
            if (holeSouthZ > 0.001f)
                AddBox(root, $"{name}_SW",
                    new Vector3(holeW * 0.5f, y, holeSouthZ * 0.5f),
                    new Vector3(holeW, wall, holeSouthZ));
            // North patch — solid floor between the top of the stairs and the wall.
            if (d - holeNorthZ > 0.001f)
                AddBox(root, $"{name}_NW",
                    new Vector3(holeW * 0.5f, y, holeNorthZ + (d - holeNorthZ) * 0.5f),
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
            const float rampX      = 1f;        // ramp centred at local x=1
            const float runStart   = 1f;
            float       runEnd     = d - 1f;
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
            const float dw = 2f;   // door opening width
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

        private static GameObject RepairDoorPrefab()
        {
            var prefabPath = $"{InteriorPrefabFolder}/InteriorDoor.prefab";
            var existing   = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            var root = new GameObject("InteriorDoor");
            root.AddComponent<NetworkObject>();
            var door = root.AddComponent<InteriorDoor>();

            var pivot = new GameObject("DoorPivot");
            pivot.transform.SetParent(root.transform);

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "DoorMesh";
            mesh.transform.SetParent(pivot.transform);
            mesh.transform.localPosition = new Vector3(1f, 1.5f, 0);
            mesh.transform.localScale    = new Vector3(2f, 3f, 0.1f);

            var col = root.AddComponent<BoxCollider>();
            col.size   = new Vector3(2f, 3f, 0.1f);
            col.center = new Vector3(1f, 1.5f, 0);

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
            so.FindProperty("gridCellMeters").floatValue    = 8f;
            so.FindProperty("minSpecialRooms").intValue     = spec.MinSpecial;
            so.FindProperty("maxSpecialRooms").intValue     = spec.MaxSpecial;

            var pool = so.FindProperty("roomPool");
            pool.arraySize = allDefs.Length;
            for (int i = 0; i < allDefs.Length; i++)
                pool.GetArrayElementAtIndex(i).objectReferenceValue = allDefs[i];

            so.ApplyModifiedPropertiesWithoutUndo();
            if (isNew) AssetDatabase.CreateAsset(def, assetPath);
            EditorUtility.SetDirty(def);
            return def;
        }

        // ── Interior catalog ───────────────────────────────────────────────────

        private static void RepairInteriorCatalog()
        {
            var catalogPath = $"{InteriorAssetFolder}/InteriorCatalog.asset";
            // Always rebuild — adding a new building spec must update the catalog.

            var guids = AssetDatabase.FindAssets("t:BuildingDefinition", new[] { InteriorBuildingFolder });
            var defs  = new List<BuildingDefinition>();
            foreach (var guid in guids)
                defs.Add(AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid)));

            var catalog   = AssetDatabase.LoadAssetAtPath<InteriorCatalog>(catalogPath);
            bool isNewCat = catalog == null;
            if (isNewCat)
                catalog = ScriptableObject.CreateInstance<InteriorCatalog>();

            var so      = new SerializedObject(catalog);
            var arr     = so.FindProperty("buildings");
            arr.arraySize = defs.Count;
            for (int i = 0; i < defs.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];
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

        private static RoomSpec[] GetRoomSpecs() => new[]
        {
            new RoomSpec("Room_Entry_1x1",   new Vector2Int(1,1), RoomCategory.Entry,   new[]{SocketDirection.North,SocketDirection.South,SocketDirection.East,SocketDirection.West}, false, 10),
            new RoomSpec("Room_Generic_1x1", new Vector2Int(1,1), RoomCategory.Generic, new[]{SocketDirection.North,SocketDirection.South,SocketDirection.East,SocketDirection.West}, false, 20),
            new RoomSpec("Room_Generic_1x2", new Vector2Int(1,2), RoomCategory.Generic, new[]{SocketDirection.North,SocketDirection.South,SocketDirection.East,SocketDirection.West}, false, 15),
            new RoomSpec("Room_Generic_2x2", new Vector2Int(2,2), RoomCategory.Generic, new[]{SocketDirection.North,SocketDirection.South,SocketDirection.East,SocketDirection.West}, false, 10),
            new RoomSpec("Room_Utility_1x1", new Vector2Int(1,1), RoomCategory.Utility, new[]{SocketDirection.South}, false, 10),
            new RoomSpec("Room_Special_2x2", new Vector2Int(2,2), RoomCategory.Special, new[]{SocketDirection.North,SocketDirection.South,SocketDirection.East,SocketDirection.West}, false, 5),
            new RoomSpec("Room_Stair_1x1",   new Vector2Int(1,1), RoomCategory.Generic, new[]{SocketDirection.North,SocketDirection.South,SocketDirection.Up,SocketDirection.Down},  true, 10),
        };

        private static BuildingSpec[] GetBuildingSpecs() => new[]
        {
            new BuildingSpec("Building_Small",      "Small Building",       4,  8,  1, 1, 0, 1),
            new BuildingSpec("Building_Medium",     "Medium Building",      8, 20,  2, 3, 1, 2),
            new BuildingSpec("Building_Large",      "Large Building",      20, 40,  3, 5, 2, 4),
            new BuildingSpec("Building_Multifloor", "Multi-floor Building",10, 18,  3, 3, 1, 2),
        };

        private readonly struct RoomSpec
        {
            public readonly string Name;
            public readonly Vector2Int GridSize;
            public readonly RoomCategory Category;
            public readonly SocketDirection[] Sockets;
            public readonly bool IsVerticalConnector;
            public readonly int Weight;

            public RoomSpec(string name, Vector2Int size, RoomCategory cat, SocketDirection[] sockets, bool vertical, int weight)
            {
                Name = name; GridSize = size; Category = cat; Sockets = sockets;
                IsVerticalConnector = vertical; Weight = weight;
            }
        }

        private readonly struct BuildingSpec
        {
            public readonly string Name, DisplayName;
            public readonly int MinRooms, MaxRooms, MinFloors, MaxFloors, MinSpecial, MaxSpecial;

            public BuildingSpec(string name, string displayName, int minR, int maxR, int minF, int maxF, int minS, int maxS)
            {
                Name = name; DisplayName = displayName;
                MinRooms = minR; MaxRooms = maxR; MinFloors = minF; MaxFloors = maxF;
                MinSpecial = minS; MaxSpecial = maxS;
            }
        }
    }
}
#endif
