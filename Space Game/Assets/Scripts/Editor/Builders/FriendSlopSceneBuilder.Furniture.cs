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
            var specs = GetAllFurnitureSpecs();
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
            var specs = GetAllFurnitureSpecs();
            var defs  = new FurnitureDefinition[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                var prefab = RepairFurniturePrefab(specs[i]);
                defs[i]    = RepairFurnitureDefinition(specs[i], prefab);
            }
            return defs;
        }

        // Combines the core catalogue with the extended residential additions.
        private static FurnitureSpec[] GetAllFurnitureSpecs()
        {
            var core = GetFurnitureSpecs();
            var ext  = GetExtendedResidentialSpecs();
            var all  = new FurnitureSpec[core.Length + ext.Length];
            System.Array.Copy(core, 0, all, 0, core.Length);
            System.Array.Copy(ext,  0, all, core.Length, ext.Length);
            return all;
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

            var topsProp = so.FindProperty("tabletopAnchors");
            topsProp.arraySize = spec.TabletopAnchors.Length;
            for (int i = 0; i < spec.TabletopAnchors.Length; i++)
            {
                var t = spec.TabletopAnchors[i];
                var elem = topsProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("localPosition").vector3Value = t.localPosition;
                elem.FindPropertyRelative("footprintXZ").vector2Value   = t.footprintXZ;
            }

            var aroundProp = so.FindProperty("aroundTableAnchors");
            aroundProp.arraySize = spec.AroundTableAnchors.Length;
            for (int i = 0; i < spec.AroundTableAnchors.Length; i++)
            {
                var a = spec.AroundTableAnchors[i];
                var elem = aroundProp.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("localPosition").vector3Value = a.localPosition;
                elem.FindPropertyRelative("footprintXZ").vector2Value   = a.footprintXZ;
                elem.FindPropertyRelative("yawDegrees").floatValue      = a.yawDegrees;
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
            public readonly TabletopAnchor[] TabletopAnchors;
            public readonly AroundTableAnchor[] AroundTableAnchors;

            public FurnitureSpec(string name, string displayName, string kind, string[] tags,
                AnchorPlacement placement, Vector2 footprint, int weight, bool interactable,
                PrimitiveBox[] primitives, TabletopAnchor[] tabletopAnchors = null,
                AroundTableAnchor[] aroundTableAnchors = null)
            {
                Name = name; DisplayName = displayName; Kind = kind; Tags = tags;
                Placement = placement; FootprintXZ = footprint;
                Weight = weight; Interactable = interactable;
                Primitives = primitives;
                TabletopAnchors = tabletopAnchors ?? System.Array.Empty<TabletopAnchor>();
                AroundTableAnchors = aroundTableAnchors ?? System.Array.Empty<AroundTableAnchor>();
            }
        }

        // Shorthand for declaring a tabletop slot. localY is the height of the table's
        // top surface (so spawned items rest on it).
        private static TabletopAnchor TopAnchor(float x, float y, float z, float fx, float fz)
            => new TabletopAnchor
            {
                localPosition = new Vector3(x, y, z),
                footprintXZ = new Vector2(fx, fz),
            };

        // Shorthand for declaring an around-table slot for a chair. yawDegrees is the
        // chair's local Y rotation — usually pointed inward at the table.
        private static AroundTableAnchor AroundAnchor(float x, float z, float yaw, float fx = 0.6f, float fz = 0.6f)
            => new AroundTableAnchor
            {
                localPosition = new Vector3(x, 0f, z),
                footprintXZ = new Vector2(fx, fz),
                yawDegrees = yaw,
            };

        private static PrimitiveBox Cube(Vector3 pos, Vector3 scale, Color tint, Vector3? euler = null)
            => new PrimitiveBox
            {
                shape = PrimitiveType.Cube,
                localPosition = pos,
                localScale = scale,
                localEulerAngles = euler ?? Vector3.zero,
                tint = tint,
            };
        private static PrimitiveBox Cyl(Vector3 pos, Vector3 scale, Color tint, Vector3? euler = null)
            => new PrimitiveBox
            {
                shape = PrimitiveType.Cylinder,
                localPosition = pos,
                localScale = scale,
                localEulerAngles = euler ?? Vector3.zero,
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
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.35f, 1.22f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0.35f, 1.22f, 0f, 0.4f, 0.4f),
                    }),
                new FurnitureSpec("Furniture_Nightstand", "Nightstand", "nightstand",
                    new[] { FurnitureTags.Bedroom },
                    AnchorPlacement.Corner, new Vector2(0.6f, 0.5f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.5f), wood),
                    },
                    tabletopAnchors: new[] { TopAnchor(0f, 0.62f, 0f, 0.4f, 0.4f) }),

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
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.5f, 0.95f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0.5f, 0.95f, 0f, 0.4f, 0.4f),
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
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.4f, 0.78f, 0f, 0.5f, 0.5f),
                        TopAnchor( 0.4f, 0.78f, 0f, 0.4f, 0.4f),
                    },
                    // One chair slot in front of the desk, facing inward toward the desk.
                    aroundTableAnchors: new[]
                    {
                        AroundAnchor(0f, 0.62f, 180f),
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

                // ── Factory / Workshop / Garage ─────────────────────────────
                new FurnitureSpec("Furniture_Workbench", "Workbench", "workbench",
                    new[] { FurnitureTags.Workshop, FurnitureTags.Factory, FurnitureTags.Garage },
                    AnchorPlacement.Wall, new Vector2(2.0f, 0.8f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.95f, 0f), new Vector3(2.0f, 0.08f, 0.8f), darkMetal),
                        Cube(new Vector3(-0.85f, 0.45f, 0f), new Vector3(0.1f, 0.95f, 0.7f), metal),
                        Cube(new Vector3( 0.85f, 0.45f, 0f), new Vector3(0.1f, 0.95f, 0.7f), metal),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.5f, 0.99f, 0f, 0.5f, 0.5f),
                        TopAnchor( 0.5f, 0.99f, 0f, 0.5f, 0.5f),
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
                    // Eating-area tag set only — a generic table sitting in the middle of a
                    // room only makes sense in dining/kitchen/break-room contexts. Non-eating
                    // rooms (LR, Office, Hallway, etc.) use Furniture_ConsoleTable along a
                    // wall instead.
                    new[] { FurnitureTags.Dining, FurnitureTags.Kitchen,
                            FurnitureTags.BreakRoom, FurnitureTags.Cafeteria },
                    AnchorPlacement.Center, new Vector2(1.6f, 1.0f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.78f, 0f), new Vector3(1.6f, 0.06f, 1.0f), wood),
                        Cube(new Vector3( 0.72f, 0.39f,  0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                        Cube(new Vector3(-0.72f, 0.39f,  0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                        Cube(new Vector3( 0.72f, 0.39f, -0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                        Cube(new Vector3(-0.72f, 0.39f, -0.42f), new Vector3(0.08f, 0.78f, 0.08f), wood),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.5f, 0.82f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0.5f, 0.82f, 0f, 0.4f, 0.4f),
                    },
                    // 4 chair slots around the table — south, north, west, east.
                    aroundTableAnchors: new[]
                    {
                        AroundAnchor( 0f, -0.78f,   0f),   // south side, chair faces +Z
                        AroundAnchor( 0f,  0.78f, 180f),   // north side, chair faces -Z
                        AroundAnchor(-1.08f, 0f,  90f),    // west side,  chair faces +X
                        AroundAnchor( 1.08f, 0f, 270f),    // east side,  chair faces -X
                    }),
                new FurnitureSpec("Furniture_Chair", "Chair", "chair",
                    // Strict eating rooms (Dining, Kitchen, Cafeteria) no longer get loose
                    // chairs — those are placed around tables via the AroundTable pass
                    // using Furniture_DiningChair. BreakRoom and Office still allow loose
                    // chairs because people need somewhere to sit besides the table.
                    new[] { FurnitureTags.Office, FurnitureTags.BreakRoom },
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

        // ── Extended residential catalogue (added in the big content pass) ───────
        // Returns the additional residential furniture pieces. Concatenated into the
        // main catalogue inside GetFurnitureSpecs via the partial helper below.
        private static FurnitureSpec[] GetExtendedResidentialSpecs()
        {
            var wood       = new Color(0.55f, 0.32f, 0.17f);
            var darkWood   = new Color(0.30f, 0.18f, 0.10f);
            var pillow     = new Color(0.85f, 0.85f, 0.92f);
            var sheet      = new Color(0.95f, 0.95f, 0.92f);
            var fabric     = new Color(0.40f, 0.45f, 0.55f);
            var redFabric  = new Color(0.55f, 0.20f, 0.20f);
            var greenFabric= new Color(0.20f, 0.40f, 0.25f);
            var metal      = new Color(0.65f, 0.65f, 0.70f);
            var darkMetal  = new Color(0.30f, 0.30f, 0.35f);
            var stainless  = new Color(0.78f, 0.80f, 0.85f);
            var porcelain  = new Color(0.98f, 0.98f, 0.95f);
            var lampShade  = new Color(0.95f, 0.86f, 0.55f);
            var glass      = new Color(0.55f, 0.75f, 0.85f);
            var screen     = new Color(0.05f, 0.05f, 0.08f);
            var book       = new Color(0.55f, 0.20f, 0.15f);
            var plantLeaf  = new Color(0.20f, 0.55f, 0.22f);
            var pot        = new Color(0.45f, 0.30f, 0.20f);
            var wine       = new Color(0.30f, 0.05f, 0.10f);
            var paper      = new Color(0.95f, 0.92f, 0.85f);
            var rust       = new Color(0.55f, 0.32f, 0.15f);
            var tile       = new Color(0.85f, 0.85f, 0.88f);
            var carRed     = new Color(0.65f, 0.15f, 0.15f);
            var black      = new Color(0.08f, 0.08f, 0.08f);
            var brick      = new Color(0.62f, 0.30f, 0.22f);
            var copper     = new Color(0.72f, 0.42f, 0.20f);

            return new[]
            {
                // ── Bedroom additions ───────────────────────────────────────────
                new FurnitureSpec("Furniture_Wardrobe", "Wardrobe", "wardrobe",
                    new[] { FurnitureTags.Bedroom, FurnitureTags.Closet },
                    AnchorPlacement.Wall, new Vector2(1.4f, 0.6f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(1.4f, 2.0f, 0.6f), wood),
                        // Two door panels with a thin gap between
                        Cube(new Vector3(-0.35f, 1.0f, 0.31f), new Vector3(0.65f, 1.9f, 0.02f), darkWood),
                        Cube(new Vector3( 0.35f, 1.0f, 0.31f), new Vector3(0.65f, 1.9f, 0.02f), darkWood),
                        Cube(new Vector3(-0.08f, 1.0f, 0.33f), new Vector3(0.04f, 0.06f, 0.04f), metal),
                        Cube(new Vector3( 0.08f, 1.0f, 0.33f), new Vector3(0.04f, 0.06f, 0.04f), metal),
                    }),
                new FurnitureSpec("Furniture_Vanity", "Vanity", "vanity",
                    new[] { FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(1.0f, 0.5f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.4f, 0f), new Vector3(1.0f, 0.8f, 0.5f), wood),
                        // Tall framed mirror behind the table
                        Cube(new Vector3(0f, 1.3f, -0.22f), new Vector3(0.8f, 1.0f, 0.04f), darkWood),
                        Cube(new Vector3(0f, 1.3f, -0.195f), new Vector3(0.7f, 0.9f, 0.005f), glass),
                    },
                    tabletopAnchors: new[] { TopAnchor(0f, 0.82f, 0f, 0.6f, 0.4f) }),
                new FurnitureSpec("Furniture_WallMirror", "Wall Mirror", "mirror",
                    new[] { FurnitureTags.Bedroom, FurnitureTags.Bathroom, FurnitureTags.Hallway,
                            FurnitureTags.Shared },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.2f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.5f, -0.08f), new Vector3(0.7f, 1.2f, 0.04f), darkWood),
                        // Glass plane sits 5mm proud of the frame's front face to avoid z-fighting.
                        Cube(new Vector3(0f, 1.5f, -0.055f), new Vector3(0.6f, 1.1f, 0.005f), glass),
                    }),
                new FurnitureSpec("Furniture_ReadingChair", "Reading Chair", "armchair",
                    new[] { FurnitureTags.Bedroom, FurnitureTags.LivingRoom, FurnitureTags.Office },
                    AnchorPlacement.Corner, new Vector2(0.9f, 0.9f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.25f, 0f),     new Vector3(0.85f, 0.4f, 0.85f), redFabric),
                        Cube(new Vector3(0f, 0.7f, -0.35f),  new Vector3(0.85f, 0.7f, 0.18f), redFabric),
                        Cube(new Vector3(-0.43f, 0.5f, 0f),  new Vector3(0.15f, 0.55f, 0.85f), redFabric),
                        Cube(new Vector3( 0.43f, 0.5f, 0f),  new Vector3(0.15f, 0.55f, 0.85f), redFabric),
                    }),
                new FurnitureSpec("Furniture_Hamper", "Hamper", "hamper",
                    new[] { FurnitureTags.Bedroom, FurnitureTags.Bathroom, FurnitureTags.Laundry },
                    AnchorPlacement.Corner, new Vector2(0.4f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.35f, 0f), new Vector3(0.4f, 0.35f, 0.4f), fabric),
                        Cyl(new Vector3(0f, 0.72f, 0f), new Vector3(0.42f, 0.03f, 0.42f), darkWood),
                    }),

                // ── Bathroom additions ─────────────────────────────────────────
                new FurnitureSpec("Furniture_ShowerStall", "Shower Stall", "shower",
                    new[] { FurnitureTags.Bathroom },
                    AnchorPlacement.Wall, new Vector2(1.0f, 1.0f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.05f, 0f), new Vector3(1.0f, 0.1f, 1.0f), tile),
                        // Glass walls (sides + front, back is the room wall)
                        Cube(new Vector3(-0.49f, 1.1f, 0f), new Vector3(0.02f, 2.0f, 1.0f), glass),
                        Cube(new Vector3( 0.49f, 1.1f, 0f), new Vector3(0.02f, 2.0f, 1.0f), glass),
                        Cube(new Vector3(0f, 1.1f, 0.49f), new Vector3(1.0f, 2.0f, 0.02f), glass),
                        // Shower head on the back wall
                        Cyl(new Vector3(0f, 1.9f, -0.4f), new Vector3(0.04f, 0.1f, 0.04f), stainless),
                        Cyl(new Vector3(0f, 1.95f, -0.3f), new Vector3(0.12f, 0.04f, 0.12f), stainless),
                    }),
                new FurnitureSpec("Furniture_BathroomMirror", "Bathroom Mirror", "mirror",
                    new[] { FurnitureTags.Bathroom },
                    AnchorPlacement.Wall, new Vector2(0.6f, 0.15f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.5f, -0.06f), new Vector3(0.6f, 0.5f, 0.03f), darkWood),
                        Cube(new Vector3(0f, 1.5f, -0.04f), new Vector3(0.55f, 0.45f, 0.005f), glass),
                    }),
                new FurnitureSpec("Furniture_TowelRack", "Towel Rack", "towelrack",
                    new[] { FurnitureTags.Bathroom },
                    AnchorPlacement.Wall, new Vector2(0.6f, 0.1f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3( 0.25f, 1.2f, -0.04f), new Vector3(0.04f, 0.04f, 0.04f), stainless),
                        Cyl(new Vector3(-0.25f, 1.2f, -0.04f), new Vector3(0.04f, 0.04f, 0.04f), stainless),
                        Cube(new Vector3(0f, 1.2f, -0.05f), new Vector3(0.55f, 0.04f, 0.04f), stainless),
                        // A folded towel hanging
                        Cube(new Vector3(0f, 1.0f, -0.08f), new Vector3(0.3f, 0.3f, 0.04f), pillow),
                    }),
                new FurnitureSpec("Furniture_ToiletPaperHolder", "Toilet Paper Holder", "tpholder",
                    new[] { FurnitureTags.Bathroom },
                    AnchorPlacement.Wall, new Vector2(0.3f, 0.1f), weight: 1, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.7f, -0.04f), new Vector3(0.06f, 0.06f, 0.05f), stainless),
                        Cyl(new Vector3(0f, 0.7f, -0.1f), new Vector3(0.12f, 0.05f, 0.12f), paper),
                    }),
                new FurnitureSpec("Furniture_BathroomTrash", "Bathroom Trash", "trash",
                    new[] { FurnitureTags.Bathroom, FurnitureTags.Office, FurnitureTags.Shared },
                    AnchorPlacement.Corner, new Vector2(0.3f, 0.3f), weight: 1, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.2f, 0.3f), darkMetal),
                        Cyl(new Vector3(0f, 0.42f, 0f), new Vector3(0.32f, 0.02f, 0.32f), metal),
                    }),

                // ── Kitchen additions ──────────────────────────────────────────
                new FurnitureSpec("Furniture_Microwave", "Microwave", "microwave",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Tabletop, new Vector2(0.5f, 0.4f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.16f, 0f),    new Vector3(0.5f, 0.32f, 0.4f), darkMetal),
                        Cube(new Vector3(-0.05f, 0.16f, 0.21f), new Vector3(0.3f, 0.22f, 0.02f), glass),
                        Cube(new Vector3( 0.18f, 0.16f, 0.21f), new Vector3(0.08f, 0.22f, 0.02f), screen),
                    }),
                new FurnitureSpec("Furniture_Dishwasher", "Dishwasher", "dishwasher",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Wall, new Vector2(0.6f, 0.6f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.6f), stainless),
                        Cube(new Vector3(0f, 0.85f, 0.31f), new Vector3(0.5f, 0.05f, 0.02f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_KitchenIsland", "Kitchen Island", "island",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Center, new Vector2(1.6f, 0.9f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.9f), wood),
                        Cube(new Vector3(0f, 0.92f, 0f), new Vector3(1.6f, 0.04f, 0.9f), porcelain),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.5f, 0.95f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0.5f, 0.95f, 0f, 0.4f, 0.4f),
                    }),
                new FurnitureSpec("Furniture_BarStool", "Bar Stool", "barstool",
                    new[] { FurnitureTags.Kitchen, FurnitureTags.GameRoom, FurnitureTags.BreakRoom },
                    AnchorPlacement.Center, new Vector2(0.4f, 0.4f), weight: 3, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.78f, 0f), new Vector3(0.36f, 0.04f, 0.36f), wood),
                        Cyl(new Vector3(0f, 0.4f, 0f), new Vector3(0.05f, 0.4f, 0.05f), metal),
                        Cyl(new Vector3(0f, 0.05f, 0f), new Vector3(0.36f, 0.04f, 0.36f), metal),
                    }),
                new FurnitureSpec("Furniture_Toaster", "Toaster", "toaster",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Tabletop, new Vector2(0.3f, 0.2f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.1f, 0f), new Vector3(0.28f, 0.18f, 0.18f), stainless),
                        Cube(new Vector3(0f, 0.2f, 0f), new Vector3(0.05f, 0.02f, 0.1f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_CoffeeMaker", "Coffee Maker", "coffeemaker",
                    new[] { FurnitureTags.Kitchen },
                    AnchorPlacement.Tabletop, new Vector2(0.3f, 0.3f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.15f, -0.05f), new Vector3(0.25f, 0.3f, 0.2f), black),
                        Cyl(new Vector3(0f, 0.1f, 0.08f), new Vector3(0.14f, 0.1f, 0.14f), glass),
                        Cyl(new Vector3(0f, 0.18f, 0.08f), new Vector3(0.08f, 0.06f, 0.08f), wine),
                    }),

                // ── Living Room / Den additions ────────────────────────────────
                new FurnitureSpec("Furniture_TV", "Television", "tv",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.BreakRoom, FurnitureTags.GameRoom },
                    AnchorPlacement.Wall, new Vector2(1.4f, 0.15f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.4f, -0.06f), new Vector3(1.4f, 0.85f, 0.05f), black),
                        Cube(new Vector3(0f, 1.4f, -0.04f), new Vector3(1.3f, 0.75f, 0.02f), screen),
                    }),
                new FurnitureSpec("Furniture_TVStand", "TV Stand", "tvstand",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.BreakRoom, FurnitureTags.GameRoom },
                    AnchorPlacement.Wall, new Vector2(1.6f, 0.45f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.6f, 0.45f), darkWood),
                        Cube(new Vector3(-0.5f, 0.3f, 0.23f), new Vector3(0.6f, 0.4f, 0.02f), wood),
                        Cube(new Vector3( 0.5f, 0.3f, 0.23f), new Vector3(0.6f, 0.4f, 0.02f), wood),
                    },
                    tabletopAnchors: new[] { TopAnchor(0f, 0.62f, 0f, 0.5f, 0.3f) }),
                new FurnitureSpec("Furniture_CoffeeTable", "Coffee Table", "coffeetable",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.BreakRoom },
                    AnchorPlacement.Center, new Vector2(1.2f, 0.7f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.04f, 0.7f), darkWood),
                        Cube(new Vector3( 0.55f, 0.2f,  0.3f), new Vector3(0.06f, 0.4f, 0.06f), darkWood),
                        Cube(new Vector3(-0.55f, 0.2f,  0.3f), new Vector3(0.06f, 0.4f, 0.06f), darkWood),
                        Cube(new Vector3( 0.55f, 0.2f, -0.3f), new Vector3(0.06f, 0.4f, 0.06f), darkWood),
                        Cube(new Vector3(-0.55f, 0.2f, -0.3f), new Vector3(0.06f, 0.4f, 0.06f), darkWood),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.35f, 0.43f, 0f, 0.3f, 0.3f),
                        TopAnchor( 0.35f, 0.43f, 0f, 0.3f, 0.3f),
                    }),
                // Wall-placed "console table" for non-eating rooms — sits flush against
                // the wall (back face at the wall plane) so the look matches how console /
                // entry / hallway tables sit in a real house.
                new FurnitureSpec("Furniture_ConsoleTable", "Console Table", "console_table",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Office, FurnitureTags.Hallway,
                            FurnitureTags.Bedroom, FurnitureTags.MudRoom },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.4f), weight: 4, interactable: false,
                    new[]
                    {
                        // Top surface — sits 0.3m forward of the anchor so the table's back
                        // face lands at the wall plane (anchor wallInset is 0.5m).
                        Cube(new Vector3(0f, 0.78f, -0.3f),       new Vector3(1.2f, 0.04f, 0.4f), wood),
                        // 4 thin legs at the corners of the top
                        Cube(new Vector3( 0.55f, 0.39f, -0.12f),  new Vector3(0.06f, 0.78f, 0.06f), wood),
                        Cube(new Vector3(-0.55f, 0.39f, -0.12f),  new Vector3(0.06f, 0.78f, 0.06f), wood),
                        Cube(new Vector3( 0.55f, 0.39f, -0.48f),  new Vector3(0.06f, 0.78f, 0.06f), wood),
                        Cube(new Vector3(-0.55f, 0.39f, -0.48f),  new Vector3(0.06f, 0.78f, 0.06f), wood),
                        // Optional lower shelf for "real console table" feel
                        Cube(new Vector3(0f, 0.2f, -0.3f),        new Vector3(1.1f, 0.03f, 0.36f), wood),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.4f, 0.82f, -0.3f, 0.3f, 0.3f),
                        TopAnchor( 0.4f, 0.82f, -0.3f, 0.3f, 0.3f),
                    }),
                new FurnitureSpec("Furniture_EndTable", "End Table", "endtable",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Bedroom },
                    AnchorPlacement.Corner, new Vector2(0.55f, 0.55f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 0.04f, 0.5f), darkWood),
                        Cyl(new Vector3(0f, 0.28f, 0f),  new Vector3(0.05f, 0.55f, 0.05f), darkWood),
                    },
                    tabletopAnchors: new[] { TopAnchor(0f, 0.58f, 0f, 0.4f, 0.4f) }),
                new FurnitureSpec("Furniture_Recliner", "Recliner", "recliner",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.BreakRoom },
                    AnchorPlacement.Corner, new Vector2(1.0f, 1.0f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.25f, 0f),     new Vector3(0.9f, 0.4f, 0.9f), fabric),
                        Cube(new Vector3(0f, 0.75f, -0.38f), new Vector3(0.9f, 0.85f, 0.18f), fabric),
                        Cube(new Vector3(-0.45f, 0.5f, 0f),  new Vector3(0.15f, 0.5f, 0.9f), fabric),
                        Cube(new Vector3( 0.45f, 0.5f, 0f),  new Vector3(0.15f, 0.5f, 0.9f), fabric),
                        Cube(new Vector3(0f, 0.18f, 0.45f),  new Vector3(0.8f, 0.16f, 0.35f), fabric),
                    }),
                new FurnitureSpec("Furniture_Rug", "Rug", "rug",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Bedroom },
                    AnchorPlacement.Center, new Vector2(1.8f, 1.2f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.01f, 0f), new Vector3(1.8f, 0.02f, 1.2f), redFabric),
                        // Border accent
                        Cube(new Vector3(0f, 0.011f, 0f), new Vector3(1.5f, 0.022f, 0.95f), pillow),
                    }),
                new FurnitureSpec("Furniture_WallArt", "Wall Art", "wallart",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Bedroom, FurnitureTags.Office,
                            FurnitureTags.Hallway, FurnitureTags.Shared },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.1f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.6f, -0.04f), new Vector3(0.7f, 0.5f, 0.04f), darkWood),
                        Cube(new Vector3(0f, 1.6f, -0.015f), new Vector3(0.6f, 0.4f, 0.005f), greenFabric),
                    }),
                new FurnitureSpec("Furniture_ThrowPillow", "Throw Pillow", "pillow_decorative",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Bedroom },
                    AnchorPlacement.Tabletop, new Vector2(0.4f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.1f, 0f), new Vector3(0.35f, 0.18f, 0.35f), redFabric),
                    }),

                // ── Dining Room additions ──────────────────────────────────────
                new FurnitureSpec("Furniture_DiningTable", "Dining Table", "dining_table",
                    new[] { FurnitureTags.Dining },
                    AnchorPlacement.Center, new Vector2(2.2f, 1.0f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.78f, 0f), new Vector3(2.2f, 0.06f, 1.0f), darkWood),
                        Cube(new Vector3( 1.0f, 0.39f,  0.42f), new Vector3(0.08f, 0.78f, 0.08f), darkWood),
                        Cube(new Vector3(-1.0f, 0.39f,  0.42f), new Vector3(0.08f, 0.78f, 0.08f), darkWood),
                        Cube(new Vector3( 1.0f, 0.39f, -0.42f), new Vector3(0.08f, 0.78f, 0.08f), darkWood),
                        Cube(new Vector3(-1.0f, 0.39f, -0.42f), new Vector3(0.08f, 0.78f, 0.08f), darkWood),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.7f, 0.82f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0f,   0.82f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0.7f, 0.82f, 0f, 0.4f, 0.4f),
                    },
                    // 6 chair slots — 2 on each long side, 1 on each short end.
                    aroundTableAnchors: new[]
                    {
                        AroundAnchor(-0.6f, -0.78f,   0f),
                        AroundAnchor( 0.6f, -0.78f,   0f),
                        AroundAnchor(-0.6f,  0.78f, 180f),
                        AroundAnchor( 0.6f,  0.78f, 180f),
                        AroundAnchor(-1.38f, 0f,    90f),
                        AroundAnchor( 1.38f, 0f,   270f),
                    }),
                new FurnitureSpec("Furniture_Buffet", "Buffet", "buffet",
                    new[] { FurnitureTags.Dining },
                    AnchorPlacement.Wall, new Vector2(1.6f, 0.5f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.5f), darkWood),
                        Cube(new Vector3(-0.4f, 0.45f, 0.26f), new Vector3(0.7f, 0.45f, 0.02f), wood),
                        Cube(new Vector3( 0.4f, 0.45f, 0.26f), new Vector3(0.7f, 0.45f, 0.02f), wood),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.5f, 0.92f, 0f, 0.4f, 0.4f),
                        TopAnchor( 0.5f, 0.92f, 0f, 0.4f, 0.4f),
                    }),
                new FurnitureSpec("Furniture_ChinaCabinet", "China Cabinet", "china_cabinet",
                    new[] { FurnitureTags.Dining },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.5f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f),  new Vector3(1.2f, 0.9f, 0.5f), darkWood),
                        // Glass display above
                        Cube(new Vector3(0f, 1.5f, 0f),   new Vector3(1.2f, 1.2f, 0.5f), darkWood),
                        Cube(new Vector3(0f, 1.5f, 0.22f), new Vector3(1.1f, 1.1f, 0.02f), glass),
                        // Shelves visible through the glass
                        Cube(new Vector3(0f, 1.2f, 0.1f), new Vector3(1.1f, 0.04f, 0.3f), wood),
                        Cube(new Vector3(0f, 1.8f, 0.1f), new Vector3(1.1f, 0.04f, 0.3f), wood),
                    }),
                new FurnitureSpec("Furniture_Chandelier", "Chandelier", "chandelier",
                    new[] { FurnitureTags.Dining, FurnitureTags.LivingRoom, FurnitureTags.Lobby },
                    AnchorPlacement.Center, new Vector2(0.8f, 0.8f), weight: 1, interactable: false,
                    new[]
                    {
                        // Stem reaching up to the ceiling
                        Cyl(new Vector3(0f, 3.0f, 0f), new Vector3(0.06f, 0.5f, 0.06f), metal),
                        Cyl(new Vector3(0f, 2.4f, 0f), new Vector3(0.5f, 0.05f, 0.5f), metal),
                        // Five "candle" bulbs around the ring
                        Cyl(new Vector3( 0.2f, 2.55f,  0f),    new Vector3(0.06f, 0.12f, 0.06f), lampShade),
                        Cyl(new Vector3(-0.2f, 2.55f,  0f),    new Vector3(0.06f, 0.12f, 0.06f), lampShade),
                        Cyl(new Vector3( 0f,   2.55f,  0.2f),  new Vector3(0.06f, 0.12f, 0.06f), lampShade),
                        Cyl(new Vector3( 0f,   2.55f, -0.2f),  new Vector3(0.06f, 0.12f, 0.06f), lampShade),
                        Cyl(new Vector3( 0f,   2.55f,  0f),    new Vector3(0.06f, 0.12f, 0.06f), lampShade),
                    }),

                // ── Office / Library additions ─────────────────────────────────
                // AroundTable placement — only spawns at a desk's chair slot, never freely.
                // Tagged for Bedroom too so a bedroom desk gets a chair pulled up to it.
                new FurnitureSpec("Furniture_OfficeChair", "Office Chair", "office_chair",
                    new[] { FurnitureTags.Office, FurnitureTags.Cubicle, FurnitureTags.Bedroom },
                    AnchorPlacement.AroundTable, new Vector2(0.6f, 0.6f), weight: 4, interactable: false,
                    new[]
                    {
                        // 5-prong base + central column + seat + back
                        Cube(new Vector3( 0.22f, 0.05f,  0f),    new Vector3(0.4f, 0.08f, 0.05f), darkMetal),
                        Cube(new Vector3(-0.22f, 0.05f,  0f),    new Vector3(0.4f, 0.08f, 0.05f), darkMetal),
                        Cube(new Vector3( 0f,    0.05f,  0.22f), new Vector3(0.05f, 0.08f, 0.4f), darkMetal),
                        Cube(new Vector3( 0f,    0.05f, -0.22f), new Vector3(0.05f, 0.08f, 0.4f), darkMetal),
                        Cyl(new Vector3(0f, 0.3f, 0f), new Vector3(0.06f, 0.4f, 0.06f), darkMetal),
                        Cube(new Vector3(0f, 0.55f, 0f),     new Vector3(0.5f, 0.08f, 0.5f), black),
                        Cube(new Vector3(0f, 0.95f, -0.22f), new Vector3(0.5f, 0.7f, 0.06f), black),
                    }),
                new FurnitureSpec("Furniture_Monitor", "Computer Monitor", "monitor",
                    new[] { FurnitureTags.Office, FurnitureTags.Cubicle, FurnitureTags.Bedroom },
                    AnchorPlacement.Tabletop, new Vector2(0.5f, 0.2f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.05f, 0f),  new Vector3(0.2f, 0.04f, 0.15f), black),
                        Cube(new Vector3(0f, 0.18f, 0f),  new Vector3(0.04f, 0.22f, 0.04f), black),
                        Cube(new Vector3(0f, 0.32f, -0.02f), new Vector3(0.5f, 0.28f, 0.04f), black),
                        Cube(new Vector3(0f, 0.32f, 0.01f),  new Vector3(0.46f, 0.24f, 0.01f), screen),
                    }),
                new FurnitureSpec("Furniture_FilingCabinet", "Filing Cabinet", "filing_cabinet",
                    new[] { FurnitureTags.Office, FurnitureTags.Cubicle },
                    AnchorPlacement.Wall, new Vector2(0.5f, 0.6f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.55f, 0f), new Vector3(0.5f, 1.1f, 0.6f), darkMetal),
                        Cube(new Vector3(0f, 0.3f,  0.31f), new Vector3(0.4f, 0.04f, 0.02f), metal),
                        Cube(new Vector3(0f, 0.65f, 0.31f), new Vector3(0.4f, 0.04f, 0.02f), metal),
                        Cube(new Vector3(0f, 1.0f, 0.31f),  new Vector3(0.4f, 0.04f, 0.02f), metal),
                    }),
                new FurnitureSpec("Furniture_WingbackChair", "Wingback Chair", "wingback",
                    new[] { FurnitureTags.Office, FurnitureTags.LivingRoom },
                    AnchorPlacement.Corner, new Vector2(0.85f, 0.85f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.25f, 0f),     new Vector3(0.8f, 0.4f, 0.8f), redFabric),
                        Cube(new Vector3(0f, 0.9f, -0.36f),  new Vector3(0.8f, 1.1f, 0.18f), redFabric),
                        // Wing sides — angled forward
                        Cube(new Vector3(-0.41f, 0.85f, -0.15f), new Vector3(0.15f, 0.95f, 0.65f), redFabric),
                        Cube(new Vector3( 0.41f, 0.85f, -0.15f), new Vector3(0.15f, 0.95f, 0.65f), redFabric),
                    }),
                new FurnitureSpec("Furniture_Globe", "Globe", "globe",
                    new[] { FurnitureTags.Office, FurnitureTags.LivingRoom },
                    AnchorPlacement.Tabletop, new Vector2(0.25f, 0.25f), weight: 1, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.04f, 0.2f), darkWood),
                        Cyl(new Vector3(0f, 0.13f, 0f), new Vector3(0.04f, 0.1f, 0.04f), darkWood),
                        Cyl(new Vector3(0f, 0.27f, 0f), new Vector3(0.22f, 0.22f, 0.22f), plantLeaf),
                    }),

                // ── Laundry additions ─────────────────────────────────────────
                new FurnitureSpec("Furniture_Washer", "Washing Machine", "washer",
                    new[] { FurnitureTags.Laundry },
                    AnchorPlacement.Wall, new Vector2(0.65f, 0.65f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f), new Vector3(0.65f, 0.9f, 0.65f), stainless),
                        Cyl(new Vector3(0f, 0.55f, 0.3f), new Vector3(0.3f, 0.05f, 0.3f), glass),
                        Cube(new Vector3(0f, 0.85f, 0f), new Vector3(0.6f, 0.06f, 0.6f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_Dryer", "Dryer", "dryer",
                    new[] { FurnitureTags.Laundry },
                    AnchorPlacement.Wall, new Vector2(0.65f, 0.65f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f), new Vector3(0.65f, 0.9f, 0.65f), pillow),
                        Cyl(new Vector3(0f, 0.45f, 0.3f), new Vector3(0.35f, 0.05f, 0.35f), darkMetal),
                        Cube(new Vector3(0f, 0.85f, 0f), new Vector3(0.6f, 0.06f, 0.6f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_IroningBoard", "Ironing Board", "ironing_board",
                    new[] { FurnitureTags.Laundry, FurnitureTags.Bedroom },
                    AnchorPlacement.Center, new Vector2(1.4f, 0.4f), weight: 1, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.85f, 0f), new Vector3(1.3f, 0.04f, 0.32f), pillow),
                        Cube(new Vector3(-0.3f, 0.42f, 0f), new Vector3(0.04f, 0.85f, 0.04f), metal),
                        Cube(new Vector3( 0.3f, 0.42f, 0f), new Vector3(0.04f, 0.85f, 0.04f), metal),
                    }),
                new FurnitureSpec("Furniture_LaundryBasket", "Laundry Basket", "laundry_basket",
                    new[] { FurnitureTags.Laundry, FurnitureTags.Bedroom, FurnitureTags.Bathroom },
                    AnchorPlacement.Corner, new Vector2(0.5f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.4f, 0.4f), pillow),
                        Cube(new Vector3(0f, 0.38f, 0f), new Vector3(0.45f, 0.05f, 0.35f), fabric),
                    }),

                // ── Pantry / Closets / Mud room ────────────────────────────────
                new FurnitureSpec("Furniture_PantryShelves", "Pantry Shelves", "pantry_shelves",
                    new[] { FurnitureTags.Kitchen, FurnitureTags.Storage },
                    AnchorPlacement.Wall, new Vector2(1.0f, 0.4f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.2f, 0f), new Vector3(1.0f, 2.4f, 0.4f), wood),
                        // Visible food jars on each shelf
                        Cyl(new Vector3(-0.3f, 0.5f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), glass),
                        Cyl(new Vector3( 0.0f, 0.5f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), copper),
                        Cyl(new Vector3( 0.3f, 0.5f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), glass),
                        Cyl(new Vector3(-0.3f, 1.1f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), copper),
                        Cyl(new Vector3( 0.0f, 1.1f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), glass),
                        Cyl(new Vector3( 0.3f, 1.1f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), copper),
                        Cyl(new Vector3(-0.3f, 1.7f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), glass),
                        Cyl(new Vector3( 0.3f, 1.7f, 0.1f), new Vector3(0.12f, 0.2f, 0.12f), copper),
                    }),
                new FurnitureSpec("Furniture_ClothesRack", "Clothes Rack", "clothes_rack",
                    new[] { FurnitureTags.Closet, FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.5f), weight: 4, interactable: false,
                    new[]
                    {
                        // Frame: two uprights + horizontal rod
                        Cyl(new Vector3(-0.55f, 0.9f, 0f), new Vector3(0.04f, 0.9f, 0.04f), metal),
                        Cyl(new Vector3( 0.55f, 0.9f, 0f), new Vector3(0.04f, 0.9f, 0.04f), metal),
                        Cube(new Vector3(0f, 1.7f, 0f), new Vector3(1.1f, 0.04f, 0.04f), metal),
                        // Hanging shirts (slim cubes)
                        Cube(new Vector3(-0.4f, 1.25f, 0f), new Vector3(0.2f, 0.6f, 0.04f), redFabric),
                        Cube(new Vector3(-0.15f, 1.25f, 0f), new Vector3(0.2f, 0.6f, 0.04f), fabric),
                        Cube(new Vector3( 0.1f, 1.25f, 0f), new Vector3(0.2f, 0.6f, 0.04f), greenFabric),
                        Cube(new Vector3( 0.35f, 1.25f, 0f), new Vector3(0.2f, 0.6f, 0.04f), pillow),
                    }),
                new FurnitureSpec("Furniture_LinenShelves", "Linen Shelves", "linen_shelves",
                    new[] { FurnitureTags.Closet, FurnitureTags.Storage },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.4f), weight: 4, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(0.8f, 2.0f, 0.4f), wood),
                        // Stacks of folded linens
                        Cube(new Vector3(0f, 0.4f, 0.05f), new Vector3(0.65f, 0.2f, 0.3f), pillow),
                        Cube(new Vector3(0f, 0.9f, 0.05f), new Vector3(0.65f, 0.2f, 0.3f), redFabric),
                        Cube(new Vector3(0f, 1.4f, 0.05f), new Vector3(0.65f, 0.2f, 0.3f), greenFabric),
                        Cube(new Vector3(0f, 1.9f, 0.05f), new Vector3(0.65f, 0.2f, 0.3f), fabric),
                    }),
                new FurnitureSpec("Furniture_ShoeRack", "Shoe Rack", "shoe_rack",
                    new[] { FurnitureTags.Closet, FurnitureTags.MudRoom, FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.3f), weight: 2, interactable: false,
                    new[]
                    {
                        // Slatted rack
                        Cube(new Vector3(0f, 0.1f, 0f), new Vector3(0.8f, 0.04f, 0.3f), darkWood),
                        Cube(new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.04f, 0.3f), darkWood),
                        // Shoes (paired blocks)
                        Cube(new Vector3(-0.25f, 0.36f,  0.05f), new Vector3(0.18f, 0.08f, 0.12f), black),
                        Cube(new Vector3(-0.25f, 0.36f, -0.05f), new Vector3(0.18f, 0.08f, 0.12f), black),
                        Cube(new Vector3( 0.25f, 0.16f,  0.05f), new Vector3(0.18f, 0.08f, 0.12f), redFabric),
                        Cube(new Vector3( 0.25f, 0.16f, -0.05f), new Vector3(0.18f, 0.08f, 0.12f), redFabric),
                    }),
                new FurnitureSpec("Furniture_CoatRack", "Coat Rack", "coat_rack",
                    new[] { FurnitureTags.MudRoom, FurnitureTags.Hallway },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.1f), weight: 4, interactable: false,
                    new[]
                    {
                        // Mounting plank
                        Cube(new Vector3(0f, 1.6f, -0.04f), new Vector3(0.8f, 0.15f, 0.04f), darkWood),
                        // Three pegs
                        Cyl(new Vector3(-0.25f, 1.6f, 0.04f), new Vector3(0.04f, 0.05f, 0.04f), metal),
                        Cyl(new Vector3( 0f,    1.6f, 0.04f), new Vector3(0.04f, 0.05f, 0.04f), metal),
                        Cyl(new Vector3( 0.25f, 1.6f, 0.04f), new Vector3(0.04f, 0.05f, 0.04f), metal),
                        // A hanging coat
                        Cube(new Vector3(-0.25f, 1.1f, 0.1f), new Vector3(0.35f, 0.8f, 0.1f), greenFabric),
                    }),
                new FurnitureSpec("Furniture_StorageBench", "Storage Bench", "bench",
                    new[] { FurnitureTags.MudRoom, FurnitureTags.Hallway, FurnitureTags.Bedroom },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.4f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.25f, 0f), new Vector3(1.2f, 0.5f, 0.4f), wood),
                        Cube(new Vector3(0f, 0.52f, 0f), new Vector3(1.2f, 0.04f, 0.4f), darkWood),
                    },
                    tabletopAnchors: new[] { TopAnchor(0f, 0.55f, 0f, 0.5f, 0.3f) }),
                new FurnitureSpec("Furniture_UmbrellaStand", "Umbrella Stand", "umbrella_stand",
                    new[] { FurnitureTags.MudRoom, FurnitureTags.Hallway },
                    AnchorPlacement.Corner, new Vector2(0.3f, 0.3f), weight: 1, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.3f, 0f), new Vector3(0.25f, 0.3f, 0.25f), copper),
                        // A folded umbrella sticking up
                        Cyl(new Vector3(0f, 0.7f, 0f), new Vector3(0.04f, 0.4f, 0.04f), black),
                    }),

                // ── Garage additions ──────────────────────────────────────────
                new FurnitureSpec("Furniture_Car", "Car", "car",
                    new[] { FurnitureTags.Garage },
                    AnchorPlacement.Center, new Vector2(4.2f, 1.8f), weight: 6, interactable: false,
                    new[]
                    {
                        // Body
                        Cube(new Vector3(0f, 0.55f, 0f), new Vector3(4.0f, 0.5f, 1.7f), carRed),
                        // Cabin
                        Cube(new Vector3(0f, 1.0f, -0.05f), new Vector3(2.4f, 0.5f, 1.55f), carRed),
                        // Windows (darker)
                        Cube(new Vector3(0f, 1.0f, 0.78f), new Vector3(2.2f, 0.4f, 0.02f), screen),
                        Cube(new Vector3(0f, 1.0f, -0.78f), new Vector3(2.2f, 0.4f, 0.02f), screen),
                        Cube(new Vector3(1.2f, 1.0f, 0f), new Vector3(0.02f, 0.4f, 1.4f), screen),
                        Cube(new Vector3(-1.2f, 1.0f, 0f), new Vector3(0.02f, 0.4f, 1.4f), screen),
                        // Wheels
                        Cyl(new Vector3( 1.4f, 0.3f,  0.85f), new Vector3(0.5f, 0.15f, 0.5f), black),
                        Cyl(new Vector3( 1.4f, 0.3f, -0.85f), new Vector3(0.5f, 0.15f, 0.5f), black),
                        Cyl(new Vector3(-1.4f, 0.3f,  0.85f), new Vector3(0.5f, 0.15f, 0.5f), black),
                        Cyl(new Vector3(-1.4f, 0.3f, -0.85f), new Vector3(0.5f, 0.15f, 0.5f), black),
                    }),
                new FurnitureSpec("Furniture_ToolChest", "Tool Chest", "tool_chest",
                    new[] { FurnitureTags.Garage, FurnitureTags.Workshop, FurnitureTags.Factory },
                    AnchorPlacement.Wall, new Vector2(0.8f, 0.5f), weight: 3, interactable: false,
                    new[]
                    {
                        // Top chest
                        Cube(new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 0.6f, 0.5f), darkMetal),
                        // Lower cabinet with castors
                        Cube(new Vector3(0f, 0.5f, 0f), new Vector3(0.8f, 0.9f, 0.5f), redFabric),
                        Cube(new Vector3(0f, 0.95f, 0.26f), new Vector3(0.7f, 0.05f, 0.02f), metal),
                        Cyl(new Vector3(-0.32f, 0.04f,  0.18f), new Vector3(0.08f, 0.04f, 0.08f), black),
                        Cyl(new Vector3( 0.32f, 0.04f,  0.18f), new Vector3(0.08f, 0.04f, 0.08f), black),
                        Cyl(new Vector3(-0.32f, 0.04f, -0.18f), new Vector3(0.08f, 0.04f, 0.08f), black),
                        Cyl(new Vector3( 0.32f, 0.04f, -0.18f), new Vector3(0.08f, 0.04f, 0.08f), black),
                    }),
                new FurnitureSpec("Furniture_ToolPegboard", "Tool Pegboard", "pegboard",
                    new[] { FurnitureTags.Garage, FurnitureTags.Workshop },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.1f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.4f, -0.04f), new Vector3(1.2f, 0.8f, 0.04f), wood),
                        // Hanging tools as colored bars
                        Cube(new Vector3(-0.4f, 1.4f, 0.03f), new Vector3(0.08f, 0.3f, 0.04f), metal),
                        Cube(new Vector3(-0.2f, 1.4f, 0.03f), new Vector3(0.08f, 0.3f, 0.04f), metal),
                        Cube(new Vector3( 0.0f, 1.4f, 0.03f), new Vector3(0.04f, 0.4f, 0.04f), redFabric),
                        Cube(new Vector3( 0.2f, 1.4f, 0.03f), new Vector3(0.1f,  0.2f, 0.04f), metal),
                        Cube(new Vector3( 0.4f, 1.4f, 0.03f), new Vector3(0.08f, 0.3f, 0.04f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_LawnMower", "Lawn Mower", "lawnmower",
                    new[] { FurnitureTags.Garage },
                    AnchorPlacement.Corner, new Vector2(0.7f, 0.6f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.35f, 0f), new Vector3(0.65f, 0.3f, 0.55f), greenFabric),
                        Cube(new Vector3(0f, 0.7f, -0.3f), new Vector3(0.04f, 0.8f, 0.04f), darkMetal),
                        Cyl(new Vector3( 0.3f, 0.12f,  0.22f), new Vector3(0.18f, 0.1f, 0.18f), black),
                        Cyl(new Vector3(-0.3f, 0.12f,  0.22f), new Vector3(0.18f, 0.1f, 0.18f), black),
                        Cyl(new Vector3( 0.3f, 0.12f, -0.22f), new Vector3(0.18f, 0.1f, 0.18f), black),
                        Cyl(new Vector3(-0.3f, 0.12f, -0.22f), new Vector3(0.18f, 0.1f, 0.18f), black),
                    }),
                new FurnitureSpec("Furniture_Bicycle", "Bicycle", "bicycle",
                    new[] { FurnitureTags.Garage },
                    AnchorPlacement.Wall, new Vector2(1.6f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        // Two wheels
                        Cyl(new Vector3(-0.55f, 0.35f, 0f), new Vector3(0.55f, 0.05f, 0.55f), black),
                        Cyl(new Vector3( 0.55f, 0.35f, 0f), new Vector3(0.55f, 0.05f, 0.55f), black),
                        // Frame (diamond)
                        Cube(new Vector3(-0.27f, 0.5f, 0f), new Vector3(0.5f,  0.05f, 0.05f), redFabric, euler: new Vector3(0, 0, 30)),
                        Cube(new Vector3( 0.27f, 0.5f, 0f), new Vector3(0.5f,  0.05f, 0.05f), redFabric, euler: new Vector3(0, 0, -30)),
                        Cube(new Vector3( 0.0f,  0.6f, 0f), new Vector3(0.55f, 0.05f, 0.05f), redFabric),
                        // Seat + handlebars
                        Cube(new Vector3(-0.35f, 0.95f, 0f), new Vector3(0.18f, 0.05f, 0.1f), black),
                        Cube(new Vector3( 0.55f, 0.9f,  0f), new Vector3(0.05f, 0.05f, 0.4f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_StorageRack", "Storage Rack", "storage_rack",
                    new[] { FurnitureTags.Garage, FurnitureTags.Storage, FurnitureTags.Basement },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.5f), weight: 4, interactable: false,
                    new[]
                    {
                        // 4 uprights
                        Cube(new Vector3(-0.55f, 1.0f,  0.22f), new Vector3(0.04f, 2.0f, 0.04f), darkMetal),
                        Cube(new Vector3( 0.55f, 1.0f,  0.22f), new Vector3(0.04f, 2.0f, 0.04f), darkMetal),
                        Cube(new Vector3(-0.55f, 1.0f, -0.22f), new Vector3(0.04f, 2.0f, 0.04f), darkMetal),
                        Cube(new Vector3( 0.55f, 1.0f, -0.22f), new Vector3(0.04f, 2.0f, 0.04f), darkMetal),
                        // Shelves
                        Cube(new Vector3(0f, 0.5f, 0f), new Vector3(1.1f, 0.04f, 0.5f), wood),
                        Cube(new Vector3(0f, 1.2f, 0f), new Vector3(1.1f, 0.04f, 0.5f), wood),
                        Cube(new Vector3(0f, 1.9f, 0f), new Vector3(1.1f, 0.04f, 0.5f), wood),
                        // Boxes on the shelves
                        Cube(new Vector3(-0.35f, 0.65f, 0f), new Vector3(0.35f, 0.25f, 0.35f), rust),
                        Cube(new Vector3( 0.3f,  0.65f, 0f), new Vector3(0.3f,  0.3f,  0.35f), pot),
                        Cube(new Vector3( 0f,    1.35f, 0f), new Vector3(0.6f,  0.25f, 0.4f),  rust),
                    }),

                // ── Game Room additions ───────────────────────────────────────
                new FurnitureSpec("Furniture_PoolTable", "Pool Table", "pool_table",
                    new[] { FurnitureTags.GameRoom },
                    AnchorPlacement.Center, new Vector2(2.4f, 1.3f), weight: 5, interactable: false,
                    new[]
                    {
                        // Slab + felt
                        Cube(new Vector3(0f, 0.78f, 0f), new Vector3(2.4f, 0.06f, 1.3f), darkWood),
                        Cube(new Vector3(0f, 0.82f, 0f), new Vector3(2.2f, 0.02f, 1.1f), greenFabric),
                        // Rails
                        Cube(new Vector3(0f,  0.84f,  0.6f), new Vector3(2.2f, 0.08f, 0.1f), darkWood),
                        Cube(new Vector3(0f,  0.84f, -0.6f), new Vector3(2.2f, 0.08f, 0.1f), darkWood),
                        Cube(new Vector3( 1.1f, 0.84f, 0f), new Vector3(0.1f, 0.08f, 1.1f), darkWood),
                        Cube(new Vector3(-1.1f, 0.84f, 0f), new Vector3(0.1f, 0.08f, 1.1f), darkWood),
                        // 6 corner/side pockets (dark recesses)
                        Cyl(new Vector3( 1.05f, 0.83f,  0.55f), new Vector3(0.15f, 0.03f, 0.15f), black),
                        Cyl(new Vector3(-1.05f, 0.83f,  0.55f), new Vector3(0.15f, 0.03f, 0.15f), black),
                        Cyl(new Vector3( 1.05f, 0.83f, -0.55f), new Vector3(0.15f, 0.03f, 0.15f), black),
                        Cyl(new Vector3(-1.05f, 0.83f, -0.55f), new Vector3(0.15f, 0.03f, 0.15f), black),
                        Cyl(new Vector3( 0f,    0.83f,  0.55f), new Vector3(0.15f, 0.03f, 0.15f), black),
                        Cyl(new Vector3( 0f,    0.83f, -0.55f), new Vector3(0.15f, 0.03f, 0.15f), black),
                        // 4 legs
                        Cube(new Vector3( 1.0f, 0.4f,  0.55f), new Vector3(0.15f, 0.78f, 0.15f), darkWood),
                        Cube(new Vector3(-1.0f, 0.4f,  0.55f), new Vector3(0.15f, 0.78f, 0.15f), darkWood),
                        Cube(new Vector3( 1.0f, 0.4f, -0.55f), new Vector3(0.15f, 0.78f, 0.15f), darkWood),
                        Cube(new Vector3(-1.0f, 0.4f, -0.55f), new Vector3(0.15f, 0.78f, 0.15f), darkWood),
                    }),
                new FurnitureSpec("Furniture_DartBoard", "Dart Board", "dartboard",
                    new[] { FurnitureTags.GameRoom },
                    AnchorPlacement.Wall, new Vector2(0.5f, 0.15f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 1.7f, -0.04f), new Vector3(0.5f, 0.04f, 0.5f), darkWood),
                        Cyl(new Vector3(0f, 1.7f, -0.02f), new Vector3(0.44f, 0.02f, 0.44f), redFabric),
                        Cyl(new Vector3(0f, 1.7f, -0.01f), new Vector3(0.1f,  0.02f, 0.1f),  greenFabric),
                    }),
                new FurnitureSpec("Furniture_Foosball", "Foosball Table", "foosball",
                    new[] { FurnitureTags.GameRoom },
                    AnchorPlacement.Center, new Vector2(1.6f, 1.0f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.88f, 0f), new Vector3(1.6f, 0.16f, 1.0f), greenFabric),
                        Cube(new Vector3(0f, 0.4f, 0f),  new Vector3(1.5f, 0.8f, 0.9f),  darkWood),
                        // Player rods (4 across the table)
                        Cube(new Vector3(-0.45f, 0.96f, 0f), new Vector3(0.04f, 0.04f, 1.2f), metal),
                        Cube(new Vector3(-0.15f, 0.96f, 0f), new Vector3(0.04f, 0.04f, 1.2f), metal),
                        Cube(new Vector3( 0.15f, 0.96f, 0f), new Vector3(0.04f, 0.04f, 1.2f), metal),
                        Cube(new Vector3( 0.45f, 0.96f, 0f), new Vector3(0.04f, 0.04f, 1.2f), metal),
                    }),
                new FurnitureSpec("Furniture_ArcadeCabinet", "Arcade Cabinet", "arcade",
                    new[] { FurnitureTags.GameRoom },
                    AnchorPlacement.Wall, new Vector2(0.7f, 0.7f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(0.7f, 2.0f, 0.7f), redFabric),
                        // Marquee at the top
                        Cube(new Vector3(0f, 1.85f, 0.3f), new Vector3(0.65f, 0.25f, 0.04f), screen),
                        // Screen
                        Cube(new Vector3(0f, 1.35f, 0.31f), new Vector3(0.55f, 0.5f, 0.04f), screen),
                        // Control panel
                        Cube(new Vector3(0f, 0.95f, 0.36f), new Vector3(0.6f, 0.05f, 0.3f), darkMetal, euler: new Vector3(20, 0, 0)),
                        Cyl(new Vector3(-0.15f, 0.99f, 0.36f), new Vector3(0.05f, 0.06f, 0.05f), redFabric),
                        Cyl(new Vector3( 0.05f, 0.99f, 0.36f), new Vector3(0.04f, 0.04f, 0.04f), greenFabric),
                        Cyl(new Vector3( 0.15f, 0.99f, 0.36f), new Vector3(0.04f, 0.04f, 0.04f), lampShade),
                    }),
                new FurnitureSpec("Furniture_HomeBar", "Home Bar", "bar",
                    new[] { FurnitureTags.GameRoom, FurnitureTags.BreakRoom },
                    AnchorPlacement.Wall, new Vector2(1.6f, 0.6f), weight: 2, interactable: false,
                    new[]
                    {
                        // Bar counter
                        Cube(new Vector3(0f, 0.55f, 0f), new Vector3(1.6f, 1.1f, 0.6f), darkWood),
                        Cube(new Vector3(0f, 1.12f, 0f), new Vector3(1.6f, 0.04f, 0.6f), wood),
                        // Backbar shelving with bottles
                        Cube(new Vector3(0f, 0.8f, -0.32f), new Vector3(1.6f, 1.6f, 0.04f), darkWood),
                        Cube(new Vector3(0f, 1.2f, -0.28f), new Vector3(1.5f, 0.04f, 0.1f), wood),
                        Cyl(new Vector3(-0.5f, 1.3f, -0.25f), new Vector3(0.08f, 0.2f, 0.08f), wine),
                        Cyl(new Vector3(-0.2f, 1.3f, -0.25f), new Vector3(0.08f, 0.2f, 0.08f), copper),
                        Cyl(new Vector3( 0.1f, 1.3f, -0.25f), new Vector3(0.08f, 0.2f, 0.08f), glass),
                        Cyl(new Vector3( 0.4f, 1.3f, -0.25f), new Vector3(0.08f, 0.2f, 0.08f), wine),
                    },
                    tabletopAnchors: new[]
                    {
                        TopAnchor(-0.5f, 1.15f, 0f, 0.3f, 0.3f),
                        TopAnchor( 0.5f, 1.15f, 0f, 0.3f, 0.3f),
                    }),

                // ── Wine Cellar additions ─────────────────────────────────────
                new FurnitureSpec("Furniture_WineRack", "Wine Rack", "wine_rack",
                    new[] { FurnitureTags.WineCellar, FurnitureTags.Storage },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.4f), weight: 5, interactable: false,
                    new[]
                    {
                        // Frame
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(1.2f, 2.0f, 0.4f), darkWood),
                        // Bottle slots: 4 rows × 5 columns of small cylinders pointing forward
                        // We'll fake it with horizontal cylinders (visible bottoms)
                        Cyl(new Vector3(-0.4f, 0.4f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.2f, 0.4f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.0f, 0.4f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.2f, 0.4f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.4f, 0.4f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.4f, 0.85f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.2f, 0.85f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.0f, 0.85f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.2f, 0.85f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.4f, 0.85f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.4f, 1.3f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.2f, 1.3f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.0f, 1.3f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.2f, 1.3f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.4f, 1.3f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.4f, 1.75f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3(-0.2f, 1.75f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.0f, 1.75f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.2f, 1.75f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                        Cyl(new Vector3( 0.4f, 1.75f, 0.12f), new Vector3(0.12f, 0.1f, 0.12f), wine),
                    }),
                new FurnitureSpec("Furniture_WineBarrel", "Wine Barrel", "wine_barrel",
                    new[] { FurnitureTags.WineCellar },
                    AnchorPlacement.Corner, new Vector2(0.7f, 0.7f), weight: 3, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.9f, 0.7f), wood),
                        // Iron bands
                        Cyl(new Vector3(0f, 0.2f, 0f), new Vector3(0.72f, 0.04f, 0.72f), darkMetal),
                        Cyl(new Vector3(0f, 0.7f, 0f), new Vector3(0.72f, 0.04f, 0.72f), darkMetal),
                        Cyl(new Vector3(0f, 0.92f, 0f), new Vector3(0.65f, 0.04f, 0.65f), darkWood),
                    }),
                new FurnitureSpec("Furniture_TastingTable", "Tasting Table", "tasting_table",
                    new[] { FurnitureTags.WineCellar, FurnitureTags.Dining },
                    AnchorPlacement.Center, new Vector2(0.9f, 0.9f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.78f, 0f), new Vector3(0.9f, 0.06f, 0.9f), darkWood),
                        Cyl(new Vector3(0f, 0.4f,  0f), new Vector3(0.1f, 0.78f, 0.1f), darkWood),
                        Cyl(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.05f, 0.5f), darkWood),
                    },
                    tabletopAnchors: new[] { TopAnchor(0f, 0.82f, 0f, 0.4f, 0.4f) }),

                // ── Workshop additions (basement) ─────────────────────────────
                new FurnitureSpec("Furniture_LumberRack", "Lumber Rack", "lumber_rack",
                    new[] { FurnitureTags.Workshop, FurnitureTags.Garage, FurnitureTags.Storage },
                    AnchorPlacement.Wall, new Vector2(1.5f, 0.3f), weight: 3, interactable: false,
                    new[]
                    {
                        // 2 uprights with 3 horizontal arms each
                        Cube(new Vector3(-0.65f, 1.0f, 0f), new Vector3(0.06f, 2.0f, 0.04f), darkMetal),
                        Cube(new Vector3( 0.65f, 1.0f, 0f), new Vector3(0.06f, 2.0f, 0.04f), darkMetal),
                        Cube(new Vector3(-0.65f, 0.5f, 0.14f), new Vector3(0.04f, 0.04f, 0.3f), darkMetal),
                        Cube(new Vector3( 0.65f, 0.5f, 0.14f), new Vector3(0.04f, 0.04f, 0.3f), darkMetal),
                        Cube(new Vector3(-0.65f, 1.2f, 0.14f), new Vector3(0.04f, 0.04f, 0.3f), darkMetal),
                        Cube(new Vector3( 0.65f, 1.2f, 0.14f), new Vector3(0.04f, 0.04f, 0.3f), darkMetal),
                        // Boards stacked on shelves
                        Cube(new Vector3(0f, 0.55f, 0.18f), new Vector3(1.5f, 0.05f, 0.18f), wood),
                        Cube(new Vector3(0f, 0.62f, 0.18f), new Vector3(1.5f, 0.04f, 0.16f), wood),
                        Cube(new Vector3(0f, 1.25f, 0.18f), new Vector3(1.5f, 0.04f, 0.16f), wood),
                    }),
                new FurnitureSpec("Furniture_Vise", "Vise", "vise",
                    new[] { FurnitureTags.Workshop, FurnitureTags.Garage },
                    AnchorPlacement.Tabletop, new Vector2(0.3f, 0.25f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.05f, 0f),  new Vector3(0.2f, 0.1f, 0.18f), darkMetal),
                        Cube(new Vector3(0f, 0.18f, -0.04f), new Vector3(0.18f, 0.15f, 0.06f), darkMetal),
                        Cube(new Vector3(0f, 0.18f,  0.05f), new Vector3(0.18f, 0.15f, 0.06f), darkMetal),
                        Cyl(new Vector3(0f, 0.18f, 0.15f), new Vector3(0.03f, 0.15f, 0.03f), metal, euler: new Vector3(90, 0, 0)),
                    }),
                new FurnitureSpec("Furniture_Sawhorse", "Sawhorse", "sawhorse",
                    new[] { FurnitureTags.Workshop, FurnitureTags.Garage },
                    AnchorPlacement.Center, new Vector2(1.2f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.8f, 0f), new Vector3(1.1f, 0.08f, 0.1f), wood),
                        Cube(new Vector3(-0.4f, 0.4f,  0.12f), new Vector3(0.06f, 0.8f, 0.06f), wood, euler: new Vector3(0, 0, -15)),
                        Cube(new Vector3(-0.4f, 0.4f, -0.12f), new Vector3(0.06f, 0.8f, 0.06f), wood, euler: new Vector3(0, 0,  15)),
                        Cube(new Vector3( 0.4f, 0.4f,  0.12f), new Vector3(0.06f, 0.8f, 0.06f), wood, euler: new Vector3(0, 0, -15)),
                        Cube(new Vector3( 0.4f, 0.4f, -0.12f), new Vector3(0.06f, 0.8f, 0.06f), wood, euler: new Vector3(0, 0,  15)),
                    }),

                // ── Mechanical Room additions (basement) ──────────────────────
                new FurnitureSpec("Furniture_Furnace", "Furnace", "furnace",
                    new[] { FurnitureTags.Mechanical, FurnitureTags.Power },
                    AnchorPlacement.Wall, new Vector2(0.9f, 0.7f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.0f, 0f), new Vector3(0.9f, 2.0f, 0.7f), stainless),
                        // Control panel front
                        Cube(new Vector3(0f, 1.0f, 0.36f), new Vector3(0.4f, 0.4f, 0.02f), darkMetal),
                        Cyl(new Vector3(-0.1f, 1.0f, 0.37f), new Vector3(0.04f, 0.02f, 0.04f), redFabric),
                        Cyl(new Vector3( 0.1f, 1.0f, 0.37f), new Vector3(0.04f, 0.02f, 0.04f), greenFabric),
                        // Vent on top
                        Cyl(new Vector3(0f, 2.15f, 0f), new Vector3(0.18f, 0.3f, 0.18f), darkMetal),
                    }),
                new FurnitureSpec("Furniture_WaterHeater", "Water Heater", "water_heater",
                    new[] { FurnitureTags.Mechanical, FurnitureTags.Power },
                    AnchorPlacement.Corner, new Vector2(0.7f, 0.7f), weight: 5, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 1.0f, 0f), new Vector3(0.65f, 2.0f, 0.65f), pillow),
                        Cyl(new Vector3(0f, 2.05f, 0f), new Vector3(0.5f, 0.1f, 0.5f), darkMetal),
                        // Pipes off the top
                        Cyl(new Vector3(-0.2f, 2.2f, 0f), new Vector3(0.04f, 0.2f, 0.04f), copper),
                        Cyl(new Vector3( 0.2f, 2.2f, 0f), new Vector3(0.04f, 0.2f, 0.04f), copper),
                    }),
                new FurnitureSpec("Furniture_ElectricalPanel", "Electrical Panel", "electrical_panel",
                    new[] { FurnitureTags.Mechanical, FurnitureTags.Power, FurnitureTags.Control },
                    AnchorPlacement.Wall, new Vector2(0.5f, 0.15f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 1.3f, -0.06f), new Vector3(0.4f, 0.6f, 0.1f), darkMetal),
                        Cube(new Vector3(0f, 1.3f, -0.02f), new Vector3(0.32f, 0.5f, 0.02f), metal),
                        // Breaker rows
                        Cube(new Vector3(-0.05f, 1.45f, -0.01f), new Vector3(0.04f, 0.06f, 0.02f), redFabric),
                        Cube(new Vector3( 0.0f,  1.45f, -0.01f), new Vector3(0.04f, 0.06f, 0.02f), redFabric),
                        Cube(new Vector3( 0.05f, 1.45f, -0.01f), new Vector3(0.04f, 0.06f, 0.02f), redFabric),
                        Cube(new Vector3(-0.05f, 1.3f, -0.01f), new Vector3(0.04f, 0.06f, 0.02f), redFabric),
                        Cube(new Vector3( 0.0f,  1.3f, -0.01f), new Vector3(0.04f, 0.06f, 0.02f), redFabric),
                        Cube(new Vector3( 0.05f, 1.3f, -0.01f), new Vector3(0.04f, 0.06f, 0.02f), redFabric),
                    }),
                new FurnitureSpec("Furniture_HVAC", "HVAC Unit", "hvac",
                    new[] { FurnitureTags.Mechanical, FurnitureTags.Power },
                    AnchorPlacement.Wall, new Vector2(1.2f, 0.6f), weight: 3, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.55f, 0f), new Vector3(1.2f, 1.1f, 0.6f), metal),
                        // Vent grille at front
                        Cube(new Vector3(0f, 0.55f, 0.31f), new Vector3(0.9f, 0.7f, 0.02f), darkMetal),
                        // Ducts off the top
                        Cyl(new Vector3(-0.3f, 1.4f, 0f), new Vector3(0.18f, 0.5f, 0.18f), stainless),
                        Cyl(new Vector3( 0.3f, 1.4f, 0f), new Vector3(0.18f, 0.5f, 0.18f), stainless),
                    }),

                // ── Universal tabletop pieces ─────────────────────────────────
                new FurnitureSpec("Furniture_TableLamp", "Table Lamp", "table_lamp",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Bedroom, FurnitureTags.Office,
                            FurnitureTags.Shared },
                    AnchorPlacement.Tabletop, new Vector2(0.3f, 0.3f), weight: 3, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.03f, 0f), new Vector3(0.25f, 0.03f, 0.25f), darkMetal),
                        Cyl(new Vector3(0f, 0.2f,  0f), new Vector3(0.04f, 0.35f, 0.04f), darkMetal),
                        Cyl(new Vector3(0f, 0.45f, 0f), new Vector3(0.3f,  0.18f, 0.3f),  lampShade),
                    }),
                new FurnitureSpec("Furniture_WallClock", "Wall Clock", "wall_clock",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Kitchen, FurnitureTags.Office,
                            FurnitureTags.Hallway, FurnitureTags.Shared },
                    AnchorPlacement.Wall, new Vector2(0.5f, 0.1f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 1.8f, -0.04f), new Vector3(0.4f, 0.04f, 0.4f), darkWood),
                        Cyl(new Vector3(0f, 1.8f, -0.025f), new Vector3(0.35f, 0.02f, 0.35f), pillow),
                        Cube(new Vector3(0f, 1.92f, -0.01f), new Vector3(0.02f, 0.12f, 0.02f), black),
                        Cube(new Vector3(0.08f, 1.8f, -0.01f), new Vector3(0.16f, 0.02f, 0.02f), black),
                    }),
                new FurnitureSpec("Furniture_TrashCan", "Trash Can", "trashcan",
                    new[] { FurnitureTags.Office, FurnitureTags.Kitchen, FurnitureTags.BreakRoom,
                            FurnitureTags.Shared },
                    AnchorPlacement.Corner, new Vector2(0.4f, 0.4f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.3f, 0f), new Vector3(0.36f, 0.3f, 0.36f), darkMetal),
                        Cyl(new Vector3(0f, 0.63f, 0f), new Vector3(0.38f, 0.04f, 0.38f), metal),
                    }),
                new FurnitureSpec("Furniture_Vase", "Vase", "vase",
                    new[] { FurnitureTags.LivingRoom, FurnitureTags.Dining, FurnitureTags.Bedroom,
                            FurnitureTags.Office, FurnitureTags.Shared },
                    AnchorPlacement.Tabletop, new Vector2(0.2f, 0.2f), weight: 2, interactable: false,
                    new[]
                    {
                        Cyl(new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.24f, 0.16f), copper),
                        Cyl(new Vector3(0f, 0.25f, 0f), new Vector3(0.12f, 0.02f, 0.12f), black),
                        // A few stems poking out
                        Cube(new Vector3(-0.03f, 0.4f, 0f), new Vector3(0.01f, 0.3f, 0.01f), plantLeaf),
                        Cube(new Vector3( 0.03f, 0.4f, 0f), new Vector3(0.01f, 0.3f, 0.01f), plantLeaf),
                        Cube(new Vector3( 0f,    0.4f, 0.03f), new Vector3(0.01f, 0.3f, 0.01f), plantLeaf),
                    }),
                // Chair that ONLY spawns around a dining/kitchen/breakroom table — slots
                // are generated at runtime from the parent table's AroundTableAnchors.
                new FurnitureSpec("Furniture_DiningChair", "Dining Chair", "dining_chair",
                    new[] { FurnitureTags.Dining, FurnitureTags.Kitchen, FurnitureTags.Cafeteria,
                            FurnitureTags.BreakRoom },
                    AnchorPlacement.AroundTable, new Vector2(0.55f, 0.55f), weight: 5, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.45f, 0f),    new Vector3(0.45f, 0.06f, 0.45f), wood),
                        Cube(new Vector3(0f, 0.75f, -0.2f), new Vector3(0.45f, 0.60f, 0.05f), wood),
                        Cube(new Vector3( 0.2f, 0.22f,  0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                        Cube(new Vector3(-0.2f, 0.22f,  0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                        Cube(new Vector3( 0.2f, 0.22f, -0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                        Cube(new Vector3(-0.2f, 0.22f, -0.2f), new Vector3(0.05f, 0.45f, 0.05f), wood),
                    }),
                new FurnitureSpec("Furniture_BooksStack", "Books", "books",
                    new[] { FurnitureTags.Office, FurnitureTags.LivingRoom, FurnitureTags.Bedroom,
                            FurnitureTags.Shared },
                    AnchorPlacement.Tabletop, new Vector2(0.25f, 0.18f), weight: 2, interactable: false,
                    new[]
                    {
                        Cube(new Vector3(0f, 0.03f, 0f),  new Vector3(0.22f, 0.05f, 0.16f), redFabric),
                        Cube(new Vector3(0f, 0.085f, 0.01f), new Vector3(0.2f,  0.05f, 0.15f), greenFabric),
                        Cube(new Vector3(0f, 0.14f, -0.01f), new Vector3(0.22f, 0.05f, 0.15f), wood),
                    }),
            };
        }
    }
}
#endif
