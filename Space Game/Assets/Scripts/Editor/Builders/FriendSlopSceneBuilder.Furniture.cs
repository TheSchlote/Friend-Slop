#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Interiors;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private const string FurniturePrefabFolder = "Assets/Prefabs/Interiors/Furniture";
        private const string FurnitureAssetFolder  = "Assets/Interiors/Furniture";

        [MenuItem("Tools/Friend Slop/Interiors/Repair Furniture Assets")]
        public static void RepairFurnitureAssets()
        {
            EnsureFurnitureFolders();
            var specs = GetFurnitureSpecs();
            var defs  = new List<FurnitureDefinition>(specs.Length);

            foreach (var spec in specs)
            {
                var prefab = RepairFurniturePrefab(spec);
                var def    = RepairFurnitureDefinition(spec, prefab);
                if (def != null) defs.Add(def);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Friend Slop] Furniture assets repaired ({defs.Count} pieces).");
        }

        // Called from RepairInteriorAssets so a single "Repair" runs everything.
        private static FurnitureDefinition[] RepairFurnitureAssetsInternal()
        {
            EnsureFurnitureFolders();
            var specs = GetFurnitureSpecs();
            var defs  = new FurnitureDefinition[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                var prefab = RepairFurniturePrefab(specs[i]);
                defs[i]    = RepairFurnitureDefinition(specs[i], prefab);
            }
            return defs;
        }

        private static FurnitureDefinition RepairFurnitureDefinition(FurnitureSpec spec, GameObject prefab)
        {
            var assetPath = $"{FurnitureAssetFolder}/{spec.Name}.asset";
            var existing  = AssetDatabase.LoadAssetAtPath<FurnitureDefinition>(assetPath);
            bool isNew    = existing == null;
            if (isNew)
                existing = ScriptableObject.CreateInstance<FurnitureDefinition>();

            var so = new SerializedObject(existing);
            so.FindProperty("displayName").stringValue   = spec.DisplayName;
            so.FindProperty("kind").stringValue          = spec.Kind ?? "";
            so.FindProperty("placement").enumValueIndex  = (int)spec.Placement;
            so.FindProperty("footprintXZ").vector2Value  = spec.FootprintXZ;
            so.FindProperty("weight").intValue           = spec.Weight;
            so.FindProperty("interactable").boolValue    = spec.Interactable;
            so.FindProperty("prefab").objectReferenceValue = prefab;

            var tagsProp = so.FindProperty("tags");
            tagsProp.arraySize = spec.Tags.Length;
            for (int i = 0; i < spec.Tags.Length; i++)
                tagsProp.GetArrayElementAtIndex(i).stringValue = spec.Tags[i];

            var primsProp = so.FindProperty("primitives");
            primsProp.arraySize = spec.Primitives.Length;
            for (int i = 0; i < spec.Primitives.Length; i++)
            {
                var p = spec.Primitives[i];
                var elem = primsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("shape").enumValueIndex   = (int)p.shape;
                elem.FindPropertyRelative("localPosition").vector3Value = p.localPosition;
                elem.FindPropertyRelative("localScale").vector3Value    = p.localScale;
                elem.FindPropertyRelative("localEulerAngles").vector3Value = p.localEulerAngles;
                elem.FindPropertyRelative("tint").colorValue            = p.tint;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            if (isNew) AssetDatabase.CreateAsset(existing, assetPath);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // Builds a placeholder prefab from the spec's primitives so the asset has a
        // tangible representation in the project. The runtime spawner can use this OR
        // build from the spec's `primitives` array directly.
        private static GameObject RepairFurniturePrefab(FurnitureSpec spec)
        {
            var prefabPath = $"{FurniturePrefabFolder}/{spec.Name}.prefab";

            var root = new GameObject(spec.Name);
            foreach (var p in spec.Primitives)
            {
                var go = GameObject.CreatePrimitive(p.shape);
                go.transform.SetParent(root.transform);
                go.transform.localPosition    = p.localPosition;
                go.transform.localScale       = p.localScale;
                go.transform.localEulerAngles = p.localEulerAngles;
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = GetOrCreateFurnitureMaterial(p.tint);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private const string FurnitureMaterialFolder = "Assets/Materials/Furniture";
        private static readonly Dictionary<Color, Material> _furnitureMaterialCache
            = new Dictionary<Color, Material>();
        private static Shader _cachedFurnitureShader;

        // Load-or-create a URP/Lit material asset per unique tint. Saving as a real
        // .mat asset is the only way the shader reference survives prefab serialization
        // reliably — inline `new Material(shader)` inside a prefab loses its shader
        // binding in URP and renders magenta.
        private static Material GetOrCreateFurnitureMaterial(Color tint)
        {
            if (_furnitureMaterialCache.TryGetValue(tint, out var cached) && cached != null)
                return cached;

            if (!AssetDatabase.IsValidFolder(FurnitureMaterialFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");
                AssetDatabase.CreateFolder("Assets/Materials", "Furniture");
            }

            string hex  = ColorUtility.ToHtmlStringRGB(tint);
            string path = $"{FurnitureMaterialFolder}/Mat_{hex}.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                if (_cachedFurnitureShader == null)
                    _cachedFurnitureShader = Shader.Find("Universal Render Pipeline/Lit")
                                          ?? Shader.Find("Standard");
                mat = new Material(_cachedFurnitureShader);
                AssetDatabase.CreateAsset(mat, path);
            }

            if (mat.color != tint) mat.color = tint;
            if (mat.HasProperty("_BaseColor") && mat.GetColor("_BaseColor") != tint)
                mat.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(mat);

            _furnitureMaterialCache[tint] = mat;
            return mat;
        }

        private static void EnsureFurnitureFolders()
        {
            foreach (var path in new[] { FurniturePrefabFolder, FurnitureAssetFolder })
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                var parts = path.Split('/');
                var parent = string.Join("/", parts, 0, parts.Length - 1);
                AssetDatabase.CreateFolder(parent, parts[parts.Length - 1]);
            }
        }

        // ── Phase-1 furniture catalogue ────────────────────────────────────────

        private readonly struct FurnitureSpec
        {
            public readonly string Name;
            public readonly string DisplayName;
            public readonly string Kind;
            public readonly string[] Tags;
            public readonly AnchorPlacement Placement;
            public readonly Vector2 FootprintXZ;
            public readonly int Weight;
            public readonly bool Interactable;
            public readonly PrimitiveBox[] Primitives;

            public FurnitureSpec(string name, string displayName, string kind, string[] tags,
                AnchorPlacement placement, Vector2 footprint, int weight, bool interactable,
                PrimitiveBox[] primitives)
            {
                Name = name; DisplayName = displayName; Kind = kind; Tags = tags;
                Placement = placement; FootprintXZ = footprint;
                Weight = weight; Interactable = interactable;
                Primitives = primitives;
            }
        }

        private static PrimitiveBox Cube(Vector3 pos, Vector3 scale, Color tint, Vector3? euler = null)
            => new PrimitiveBox
            {
                shape = PrimitiveType.Cube,
                localPosition = pos,
                localScale = scale,
                localEulerAngles = euler ?? Vector3.zero,
                tint = tint,
            };
        private static PrimitiveBox Cyl(Vector3 pos, Vector3 scale, Color tint)
            => new PrimitiveBox
            {
                shape = PrimitiveType.Cylinder,
                localPosition = pos,
                localScale = scale,
                tint = tint,
            };

        private static FurnitureSpec[] GetFurnitureSpecs()
        {
            var wood       = new Color(0.55f, 0.32f, 0.17f);
            var sheet      = new Color(0.95f, 0.95f, 0.92f);
            var pillow     = new Color(0.85f, 0.85f, 0.92f);
            var metal      = new Color(0.65f, 0.65f, 0.70f);
            var darkMetal  = new Color(0.30f, 0.30f, 0.35f);
            var plantLeaf  = new Color(0.20f, 0.55f, 0.22f);
            var pot        = new Color(0.45f, 0.30f, 0.20f);
            var lampShade  = new Color(0.95f, 0.86f, 0.55f);
            var fabric     = new Color(0.40f, 0.45f, 0.55f);
            var porcelain  = new Color(0.98f, 0.98f, 0.95f);
            var stainless  = new Color(0.78f, 0.80f, 0.85f);
            var book       = new Color(0.55f, 0.20f, 0.15f);

            return new[]
            {
                // ── Bedroom ─────────────────────────────────────────────────
                // Bed sits with headboard at -Z (wall side), foot at +Z (into room).
                new FurnitureSpec("Furniture_Bed", "Bed", "bed",
                    new[] { FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(2.0f, 1.4f), weight: 5, interactable: false,
                    new[]
                    {
                        // frame
                        Cube(new Vector3(0f, 0.2f, 0f),         new Vector3(2.0f, 0.4f, 1.4f),  wood),
                        // mattress
                        Cube(new Vector3(0f, 0.5f, 0.05f),      new Vector3(1.9f, 0.2f, 1.25f), sheet),
                        // headboard (tall plank against the wall)
                        Cube(new Vector3(0f, 0.95f, -0.65f),    new Vector3(2.0f, 1.1f, 0.1f),  wood),
                        // footboard (short plank at the foot)
                        Cube(new Vector3(0f, 0.45f, 0.68f),     new Vector3(2.0f, 0.3f, 0.08f), wood),
                        // pillows (two, at headboard end)
                        Cube(new Vector3(-0.45f, 0.65f, -0.4f), new Vector3(0.65f, 0.15f, 0.4f), pillow),
                        Cube(new Vector3( 0.45f, 0.65f, -0.4f), new Vector3(0.65f, 0.15f, 0.4f), pillow),
                        // blanket fold across the foot
                        Cube(new Vector3(0f, 0.65f, 0.3f),      new Vector3(1.9f, 0.08f, 0.55f), fabric),
                    }),
                new FurnitureSpec("Furniture_Dresser", "Dresser", "dresser",
                    new[] { FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.5f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 0.5f), wood),
                        Cube(new Vector3(0f, 0.4f, 0.26f), new Vector3(1.0f, 0.05f, 0.02f), metal),
                        Cube(new Vector3(0f, 0.8f, 0.26f), new Vector3(1.0f, 0.05f, 0.02f), metal),
                    }),
                new FurnitureSpec("Furniture_Nightstand", "Nightstand", "nightstand",
                    new[] { FurnitureTags.Bedroom },
                    AnchorPlacement.Corner, new Vector2(0.6f, 0.5f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.5f), wood),
                    }),

                // ── Bathroom ────────────────────────────────────────────────
                // Toilet: tank against the wall (-Z), bowl forward (+Z).
                new FurnitureSpec("Furniture_Toilet", "Toilet", "toilet",
                    new[] { FurnitureTags.Bathroom },
                    AnchorPlacement.Wall, new Vector2(0.7f, 0.7f), weight: 5, interactable: false,
                    new[]
                    {
                        // pedestal under the bowl
                        Cube(new Vector3(0f, 0.15f, 0.05f),  new Vector3(0.35f, 0.3f, 0.35f), porcelain),
                        // bowl rim (flat cylinder disc)
                        Cyl (new Vector3(0f, 0.33f, 0.05f),  new Vector3(0.55f, 0.04f, 0.55f), porcelain),
                        // bowl opening (dark recess so the rim reads)
                        Cube(new Vector3(0f, 0.34f, 0.05f),  new Vector3(0.42f, 0.02f, 0.42f), darkMetal),
                        // tank against the wall
                        Cube(new Vector3(0f, 0.6f, -0.27f),  new Vector3(0.55f, 0.55f, 0.2f), porcelain),
                        // tank lid
                        Cube(new Vector3(0f, 0.9f, -0.27f),  new Vector3(0.58f, 0.04f, 0.22f), porcelain),
                        // flush button
                        Cube(new Vector3(0f, 0.92f, -0.18f), new Vector3(0.08f, 0.05f, 0.05f), darkMetal),
                    }),
                // Sink: pedestal column + basin + faucet + mirror on the wall.
                new FurnitureSpec("Furniture_Sink", "Sink", "sink",
                    new[] { FurnitureTags.Bathroom, FurnitureTags.Kitchen },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.5f), weight: 4, interactable: false,
                    new[]
                    {
                        // narrow pedestal column
                        Cube(new Vector3(0f, 0.4f, 0f),       new Vector3(0.3f, 0.8f, 0.3f), porcelain),
                        // basin slab on top
                        Cube(new Vector3(0f, 0.85f, 0.02f),   new Vector3(0.8f, 0.12f, 0.45f), porcelain),
                        // basin recess (dark insert to read as a hole)
                        Cube(new Vector3(0f, 0.92f, 0.02f),   new Vector3(0.6f, 0.04f, 0.32f), darkMetal),
                        // faucet base
                        Cube(new Vector3(0f, 0.97f, -0.16f),  new Vector3(0.08f, 0.08f, 0.08f), stainless),
                        // faucet neck (vertical cylinder)
                        Cyl (new Vector3(0f, 1.08f, -0.16f),  new Vector3(0.04f, 0.12f, 0.04f), stainless),
                        // faucet spout reaching over the basin
                        Cube(new Vector3(0f, 1.16f, -0.05f),  new Vector3(0.04f, 0.04f, 0.22f), stainless),
                        // mirror mounted on the wall above
                        Cube(new Vector3(0f, 1.45f, -0.24f),  new Vector3(0.6f, 0.45f, 0.02f), pillow),
                    }),
                new FurnitureSpec("Furniture_Bathtub", "Bathtub", "bathtub",
                    new[] { FurnitureTags.Bathroom },
                    AnchorPlacement.Wall, new Vector2(1.7f, 0.8f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.3f, 0f), new Vector3(1.7f, 0.6f, 0.8f), porcelain),
                    }),

                // ── Kitchen ─────────────────────────────────────────────────
                new FurnitureSpec("Furniture_Stove", "Stove", "stove",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Wall, new Vector2(0.7f, 0.7f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.9f, 0.7f), stainless),
                        Cyl (new Vector3( 0.2f, 0.92f,  0.2f), new Vector3(0.18f, 0.02f, 0.18f), darkMetal),
                        Cyl (new Vector3(-0.2f, 0.92f,  0.2f), new Vector3(0.18f, 0.02f, 0.18f), darkMetal),
                        Cyl (new Vector3( 0.2f, 0.92f, -0.2f), new Vector3(0.18f, 0.02f, 0.18f), darkMetal),
                        Cyl (new Vector3(-0.2f, 0.92f, -0.2f), new Vector3(0.18f, 0.02f, 0.18f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_Fridge", "Refrigerator", "fridge",
                    new[] { FurnitureTags.Kitchen, FurnitureTags.BreakRoom, FurnitureTags.Cafeteria },
                    AnchorPlacement.Wall, new Vector2(0.7f, 0.7f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 1.8f, 0.7f), stainless),
                        Cube(new Vector3( 0.3f, 1.3f, 0.36f), new Vector3(0.05f, 0.5f, 0.02f), darkMetal),
                        Cube(new Vector3( 0.3f, 0.5f, 0.36f), new Vector3(0.05f, 0.5f, 0.02f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_Counter", "Counter", "counter",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Wall, new Vector2(1.5f, 0.6f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f),  new Vector3(1.5f, 0.9f, 0.6f), wood),
                        Cube(new Vector3(0f, 0.92f, 0f),  new Vector3(1.5f, 0.04f, 0.6f), porcelain),
                    }),

                // ── Office / Cubicle ────────────────────────────────────────
                new FurnitureSpec("Furniture_Desk", "Desk", "desk",
                    new[] { FurnitureTags.Office, FurnitureTags.Cubicle, FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(1.4f, 0.7f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.74f, 0f), new Vector3(1.4f, 0.05f, 0.7f), wood),
                        Cube(new Vector3( 0.6f, 0.37f,  0.3f), new Vector3(0.06f, 0.74f, 0.06f), wood),
                        Cube(new Vector3(-0.6f, 0.37f,  0.3f), new Vector3(0.06f, 0.74f, 0.06f), wood),
                        Cube(new Vector3( 0.6f, 0.37f, -0.3f), new Vector3(0.06f, 0.74f, 0.06f), wood),
                        Cube(new Vector3(-0.6f, 0.37f, -0.3f), new Vector3(0.06f, 0.74f, 0.06f), wood),
                    }),
                new FurnitureSpec("Furniture_Bookshelf", "Bookshelf", "bookshelf",
                    new[] { FurnitureTags.Office, FurnitureTags.LivingRoom, FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(1.0f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(1.0f, 2.0f, 0.4f), wood),
                        Cube(new Vector3(0f, 0.4f, 0f), new Vector3(0.95f, 0.05f, 0.35f), book),
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(0.95f, 0.05f, 0.35f), book),
                        Cube(new Vector3(0f, 1.6f, 0f), new Vector3(0.95f, 0.05f, 0.35f), book),
                    }),

                // ── Factory ─────────────────────────────────────────────────
                new FurnitureSpec("Furniture_Workbench", "Workbench", "workbench",
                    new[] { FurnitureTags.Workshop, FurnitureTags.Factory },
                    AnchorPlacement.Wall, new Vector2(2.0f, 0.8f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.95f, 0f), new Vector3(2.0f, 0.08f, 0.8f), darkMetal),
                        Cube(new Vector3(-0.85f, 0.45f, 0f), new Vector3(0.1f, 0.95f, 0.7f), metal),
                        Cube(new Vector3( 0.85f, 0.45f, 0f), new Vector3(0.1f, 0.95f, 0.7f), metal),
                    }),
                new FurnitureSpec("Furniture_Locker", "Locker", "locker",
                    new[] { FurnitureTags.Locker, FurnitureTags.Factory, FurnitureTags.Office },
                    AnchorPlacement.Wall, new Vector2(0.5f, 0.5f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 1.8f, 0.5f), darkMetal),
                        Cube(new Vector3(0f, 1.0f, 0.26f), new Vector3(0.4f, 0.05f, 0.02f), metal),
                    }),

                // ── Shared / decorative ─────────────────────────────────────
                new FurnitureSpec("Furniture_Table", "Table", "table",
                    new[] { FurnitureTags.Shared, FurnitureTags.Dining, FurnitureTags.Kitchen,
                            FurnitureTags.BreakRoom, FurnitureTags.Cafeteria },
                    AnchorPlacement.Center, new Vector2(1.6f, 1.0f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.78f, 0f), new Vector3(1.6f, 0.06f, 1.0f), wood),
                        Cube(new Vector3( 0.72f, 0.39f,  0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                        Cube(new Vector3(-0.72f, 0.39f,  0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                        Cube(new Vector3( 0.72f, 0.39f, -0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                        Cube(new Vector3(-0.72f, 0.39f, -0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                    }),
                new FurnitureSpec("Furniture_Chair", "Chair", "chair",
                    new[] { FurnitureTags.Shared, FurnitureTags.Dining, FurnitureTags.Office,
                            FurnitureTags.BreakRoom, FurnitureTags.Cafeteria },
                    AnchorPlacement.Center, new Vector2(0.55f, 0.55f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f),    new Vector3(0.45f, 0.06f, 0.45f), wood),
                        Cube(new Vector3(0f, 0.75f, -0.2f), new Vector3(0.45f, 0.60f, 0.05f), wood),
                        Cube(new Vector3( 0.2f, 0.22f,  0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                        Cube(new Vector3(-0.2f, 0.22f,  0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                        Cube(new Vector3( 0.2f, 0.22f, -0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                        Cube(new Vector3(-0.2f, 0.22f, -0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                    }),
                new FurnitureSpec("Furniture_FloorLamp", "Floor Lamp", "lamp",
                    new[] { FurnitureTags.Shared },
                    AnchorPlacement.Corner, new Vector2(0.4f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.05f, 0f), new Vector3(0.4f, 0.05f, 0.4f), metal),
                        Cyl(new Vector3(0f, 0.85f, 0f), new Vector3(0.06f, 0.8f, 0.06f),  metal),
                        Cyl(new Vector3(0f, 1.65f, 0f), new Vector3(0.5f, 0.15f, 0.5f),  lampShade),
                    }),
                new FurnitureSpec("Furniture_Plant", "Potted Plant", "plant",
                    new[] { FurnitureTags.Shared, FurnitureTags.Lobby, FurnitureTags.Office,
                            FurnitureTags.LivingRoom },
                    AnchorPlacement.Corner, new Vector2(0.5f, 0.5f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.25f, 0.5f), pot),
                        Cyl(new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 0.6f, 0.7f), plantLeaf),
                    }),
                new FurnitureSpec("Furniture_Couch", "Couch", "couch",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Lobby, FurnitureTags.BreakRoom },
                    AnchorPlacement.Wall, new Vector2(2.0f, 0.9f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.25f, 0f),    new Vector3(2.0f, 0.5f, 0.9f), fabric),
                        Cube(new Vector3(0f, 0.55f, -0.32f), new Vector3(2.0f, 0.6f, 0.2f), fabric),
                        Cube(new Vector3(-0.9f, 0.45f, 0f), new Vector3(0.2f, 0.4f, 0.9f), fabric),
                        Cube(new Vector3( 0.9f, 0.45f, 0f), new Vector3(0.2f, 0.4f, 0.9f), fabric),
                    }),
            };
        }
    }
}
#endif
