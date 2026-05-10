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
            if (existing != null) return existing;

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

            var prefab = RepairRoomPrefab(spec);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(def, assetPath);
            return def;
        }

        private static GameObject RepairRoomPrefab(RoomSpec spec)
        {
            var prefabPath = $"{InteriorRoomFolder}/{spec.Name}.prefab";
            var existing   = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            var root = new GameObject(spec.Name);
            BuildRoomGeometry(root, spec);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildRoomGeometry(GameObject root, RoomSpec spec)
        {
            float w = spec.GridSize.x * 8f;  // 8 m per grid cell
            float d = spec.GridSize.y * 8f;
            const float h = 4f;
            const float wall = 0.2f;

            AddBox(root, "Floor",   new Vector3(w * 0.5f, 0,       d * 0.5f), new Vector3(w, wall, d));
            AddBox(root, "Ceiling", new Vector3(w * 0.5f, h,       d * 0.5f), new Vector3(w, wall, d));

            var sockets = new HashSet<SocketDirection>(spec.Sockets);
            if (!sockets.Contains(SocketDirection.North))
                AddBox(root, "WallN", new Vector3(w * 0.5f, h * 0.5f, d), new Vector3(w, h, wall));
            if (!sockets.Contains(SocketDirection.South))
                AddBox(root, "WallS", new Vector3(w * 0.5f, h * 0.5f, 0), new Vector3(w, h, wall));
            if (!sockets.Contains(SocketDirection.East))
                AddBox(root, "WallE", new Vector3(w, h * 0.5f, d * 0.5f), new Vector3(wall, h, d));
            if (!sockets.Contains(SocketDirection.West))
                AddBox(root, "WallW", new Vector3(0, h * 0.5f, d * 0.5f), new Vector3(wall, h, d));

            // Door frames for each open socket (horizontal)
            foreach (var s in spec.Sockets)
            {
                if (s.IsVertical()) continue;
                AddDoorFrame(root, s, w, d, h, wall);
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

        private static void AddDoorFrame(GameObject root, SocketDirection s, float w, float d, float h, float wall)
        {
            const float dw = 2f;  // door width
            const float dh = 3f;  // door height
            float roomDim = s == SocketDirection.North || s == SocketDirection.South ? w : d;
            float wallZ   = s == SocketDirection.North ? d : (s == SocketDirection.South ? 0 : d * 0.5f);
            float wallX   = s == SocketDirection.East  ? w : (s == SocketDirection.West  ? 0 : w * 0.5f);

            bool ns = s == SocketDirection.North || s == SocketDirection.South;
            Vector3 frameScale = ns ? new Vector3(roomDim, wall, wall) : new Vector3(wall, wall, roomDim);
            Vector3 center     = new Vector3(wallX, h * 0.5f, wallZ);

            // Side pillars
            float sideW = (roomDim - dw) * 0.5f;
            if (ns)
            {
                AddBox(root, $"Frame_{s}_L", new Vector3(sideW * 0.5f,         h * 0.5f, wallZ), new Vector3(sideW, h, wall));
                AddBox(root, $"Frame_{s}_R", new Vector3(roomDim - sideW * 0.5f, h * 0.5f, wallZ), new Vector3(sideW, h, wall));
                AddBox(root, $"Frame_{s}_T", new Vector3(roomDim * 0.5f, dh + (h - dh) * 0.5f, wallZ), new Vector3(dw, h - dh, wall));
            }
            else
            {
                AddBox(root, $"Frame_{s}_L", new Vector3(wallX, h * 0.5f, sideW * 0.5f),          new Vector3(wall, h, sideW));
                AddBox(root, $"Frame_{s}_R", new Vector3(wallX, h * 0.5f, roomDim - sideW * 0.5f), new Vector3(wall, h, sideW));
                AddBox(root, $"Frame_{s}_T", new Vector3(wallX, dh + (h - dh) * 0.5f, roomDim * 0.5f), new Vector3(wall, h - dh, dw));
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
            var existing  = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(assetPath);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<BuildingDefinition>();
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
            AssetDatabase.CreateAsset(def, assetPath);
            return def;
        }

        // ── Interior catalog ───────────────────────────────────────────────────

        private static void RepairInteriorCatalog()
        {
            var catalogPath = $"{InteriorAssetFolder}/InteriorCatalog.asset";
            if (AssetDatabase.LoadAssetAtPath<InteriorCatalog>(catalogPath) != null) return;

            var guids = AssetDatabase.FindAssets("t:BuildingDefinition", new[] { InteriorBuildingFolder });
            var defs  = new List<BuildingDefinition>();
            foreach (var guid in guids)
                defs.Add(AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid)));

            var catalog = ScriptableObject.CreateInstance<InteriorCatalog>();
            var so      = new SerializedObject(catalog);
            var arr     = so.FindProperty("buildings");
            arr.arraySize = defs.Count;
            for (int i = 0; i < defs.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(catalog, catalogPath);
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
            new BuildingSpec("Building_Small",  "Small Building",       4,  8,  1, 1, 0, 1),
            new BuildingSpec("Building_Medium", "Medium Building",      8, 20,  2, 3, 1, 2),
            new BuildingSpec("Building_Large",  "Large Building",      20, 40,  3, 5, 2, 4),
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
