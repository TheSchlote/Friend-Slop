#if UNITY_EDITOR
using System.IO;
using FriendSlop.Round;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    // Editor-only builder that mirrors FlatTestWorldEnvironment's runtime layout into an
    // authored scene. The runtime env is built procedurally on the host so authors can't
    // click-edit the showcase prefabs in playmode; this scene gives them an inspector-
    // friendly view of every model the flat test world spawns.
    //
    // Workflow:
    //   1. Run "Tools/Friend Slop/Build Test World Showcase Scene" once.
    //   2. Open Assets/Scenes/TestWorld_Showcase.unity.
    //   3. Click any prefab instance to inspect/edit; use "Overrides → Apply All" to push
    //      changes back to the prefab asset. The showcase only renders one instance per
    //      prefab, so any visible edit is the canonical one.
    //   4. Re-run the menu to refresh the layout when new prefabs are added to
    //      TestWorldDisplaySet. The whole "Showcase Root" is rebuilt; environmental
    //      props (ground, light, camera) are left alone if already present.
    //
    // The scene is purely an editing affordance - it isn't added to Build Settings, doesn't
    // run any networking, and doesn't need to. Test Mode in the live game still uses the
    // procedural runtime env, which keeps using the same prefab references.
    public static class FriendSlopBuildTestWorldScene
    {
        private const string ScenePath = "Assets/Scenes/TestWorld_Showcase.unity";
        private const string DisplaySetPath = "Assets/Planets/TestWorldDisplaySet.asset";

        // Layout constants intentionally mirror FlatTestWorldEnvironment so the authored
        // scene matches what players actually see in-game. Keep these in sync if the
        // runtime numbers move - drifting them apart would defeat the point of the scene.
        private const float GroundScale = 8f;
        private const float LaunchpadRadius = 4.4f;
        private const float LaunchpadHeightOffset = 0.04f;
        private const float TeleporterOffset = LaunchpadRadius + 4f;
        private const float ShipDisplayOffsetX = -18f;
        private const int ShowcaseColumnsPerRow = 5;
        private const float ShowcaseColumnSpacing = 3.2f;
        private const float ShowcaseRowSpacing = 3.2f;
        private const float ShowcaseStandHeight = 1.1f;
        private const float ShowcaseStartZ = -10f;
        private const float ShowcaseLabelHeight = 1.6f;

        private const string ShowcaseRootName = "Showcase Root";
        private const string EnvironmentRootName = "Environment Root";

        [MenuItem("Tools/Friend Slop/Build Test World Showcase Scene")]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var displaySet = AssetDatabase.LoadAssetAtPath<TestWorldDisplaySet>(DisplaySetPath);
            if (displaySet == null)
            {
                EditorUtility.DisplayDialog("Friend Slop",
                    $"Could not find display set at '{DisplaySetPath}'. The flat test world " +
                    "needs that asset before this scene can be populated.",
                    "OK");
                return;
            }

            EnsureTargetSceneExists(out var scene);

            // Re-bind to the loaded scene by path - EditorSceneManager.NewScene returns a
            // handle that's invalidated by the save below.
            scene = SceneManager.GetSceneByPath(ScenePath);

            EnsureEnvironmentRoot(scene);
            BuildShowcaseRoot(scene, displaySet);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var prefabCount = CountPrefabs(displaySet);
            EditorUtility.DisplayDialog("Friend Slop",
                $"Built {ScenePath}.\n\n" +
                $"Showcase: {prefabCount} prefab instance(s).\n" +
                "Open the scene and edit prefabs in place; use Overrides → Apply All to " +
                "push changes back to the prefab asset.\n\n" +
                "Re-run this menu after adding new prefabs to TestWorldDisplaySet.",
                "OK");
        }

        private static void EnsureTargetSceneExists(out Scene scene)
        {
            if (File.Exists(ScenePath))
            {
                var existing = SceneManager.GetSceneByPath(ScenePath);
                scene = existing.isLoaded
                    ? existing
                    : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return;
            }

            var dir = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        // Idempotent: only creates ground / light / camera markers if missing. Re-running
        // never wipes manual layout tweaks the author might have made to lighting or camera
        // framing - the only thing that always rebuilds is the Showcase Root.
        private static void EnsureEnvironmentRoot(Scene scene)
        {
            var root = FindRoot(scene, EnvironmentRootName);
            if (root == null)
            {
                root = new GameObject(EnvironmentRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            EnsureChild(root.transform, "Flat Ground", () =>
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.localPosition = Vector3.zero;
                ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
                ApplyMaterial(ground, new Color(0.32f, 0.34f, 0.38f), emissive: false);
                return ground;
            });

            EnsureChild(root.transform, "Launchpad", () =>
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.transform.localPosition = new Vector3(0f, LaunchpadHeightOffset, 0f);
                pad.transform.localScale = new Vector3(LaunchpadRadius, 0.08f, LaunchpadRadius);
                ApplyMaterial(pad, new Color(0.85f, 0.78f, 0.32f), emissive: true);
                return pad;
            });

            EnsureChild(root.transform, "Ship Return Teleporter", () =>
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.transform.localPosition = new Vector3(TeleporterOffset, LaunchpadHeightOffset, 0f);
                pad.transform.localScale = new Vector3(2f, 0.06f, 2f);
                ApplyMaterial(pad, new Color(1f, 0.55f, 0.3f), emissive: true);
                return pad;
            });

            EnsureChild(root.transform, "Ship Display", BuildShipDisplay);
        }

        private static void BuildShowcaseRoot(Scene scene, TestWorldDisplaySet displaySet)
        {
            // Destructive on purpose - prefab list changes need a clean rebuild so removed
            // entries don't linger and indices stay aligned with the runtime layout.
            var existing = FindRoot(scene, ShowcaseRootName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var root = new GameObject(ShowcaseRootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            var index = 0;
            foreach (var section in displaySet.Sections)
            {
                if (section?.prefabs == null) continue;
                for (var i = 0; i < section.prefabs.Length; i++)
                {
                    var prefab = section.prefabs[i];
                    if (prefab == null) continue;
                    SpawnDisplay(root.transform, prefab, section.label, index);
                    index++;
                }
            }
        }

        private static void SpawnDisplay(Transform parent, GameObject prefab, string sectionLabel, int index)
        {
            var col = index % ShowcaseColumnsPerRow;
            var row = index / ShowcaseColumnsPerRow;
            var localX = (col - (ShowcaseColumnsPerRow - 1) * 0.5f) * ShowcaseColumnSpacing;
            var localZ = ShowcaseStartZ - row * ShowcaseRowSpacing;

            var slot = new GameObject($"Display [{sectionLabel}] {prefab.name}");
            slot.transform.SetParent(parent, worldPositionStays: false);
            slot.transform.localPosition = new Vector3(localX, 0f, localZ);

            // PrefabUtility keeps the link back to the prefab asset, so edits in the scene
            // can be applied as overrides - that's the whole point of using a scene here.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot.transform);
            instance.transform.localPosition = new Vector3(0f, ShowcaseStandHeight, 0f);
            instance.transform.localRotation = Quaternion.identity;

            CreateLabel(slot.transform, prefab.name);
        }

        private static void CreateLabel(Transform parent, string text)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, ShowcaseStandHeight + ShowcaseLabelHeight, 0f);
            labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var mesh = labelGo.AddComponent<TextMesh>();
            mesh.text = text.Replace('_', ' ');
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 60;
            mesh.characterSize = 0.05f;
            mesh.color = new Color(1f, 1f, 1f, 0.95f);

            var renderer = labelGo.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static GameObject BuildShipDisplay()
        {
            // Same primitive silhouette as FlatTestWorldEnvironment.ShipDisplay - lifted
            // verbatim so the editor scene matches what players see in-game. If the runtime
            // ship visuals change, mirror those edits here.
            var shipRoot = new GameObject("Ship Display");
            shipRoot.transform.localPosition = new Vector3(ShipDisplayOffsetX, 0f, 0f);

            var hullColor = new Color(0.78f, 0.80f, 0.84f);
            var accentColor = new Color(0.42f, 0.55f, 0.78f);
            var enginePlume = new Color(1f, 0.62f, 0.22f);

            CreateShipPrimitive("Ship Engine Bell", shipRoot.transform,
                PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), Quaternion.identity,
                new Vector3(2.6f, 0.6f, 2.6f), enginePlume, emissive: true);
            CreateShipPrimitive("Ship Body", shipRoot.transform,
                PrimitiveType.Cylinder, new Vector3(0f, 4.4f, 0f), Quaternion.identity,
                new Vector3(2.4f, 3.6f, 2.4f), hullColor, emissive: false);
            CreateShipPrimitive("Ship Nose", shipRoot.transform,
                PrimitiveType.Capsule, new Vector3(0f, 9.4f, 0f), Quaternion.identity,
                new Vector3(2.0f, 1.6f, 2.0f), hullColor, emissive: false);

            for (var i = 0; i < 4; i++)
            {
                var angle = i * 90f;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var finPos = dir * 1.6f + new Vector3(0f, 1.4f, 0f);
                var finRot = Quaternion.Euler(0f, angle, 12f);
                CreateShipPrimitive($"Ship Fin {i + 1}", shipRoot.transform,
                    PrimitiveType.Cube, finPos, finRot,
                    new Vector3(0.18f, 2.0f, 1.6f), accentColor, emissive: false);
            }

            CreateShipPrimitive("Ship Cockpit Window", shipRoot.transform,
                PrimitiveType.Cube, new Vector3(0f, 7.2f, 1.18f), Quaternion.identity,
                new Vector3(1.4f, 0.5f, 0.18f), accentColor, emissive: true);

            return shipRoot;
        }

        private static void CreateShipPrimitive(string name, Transform parent,
            PrimitiveType type, Vector3 localPosition, Quaternion localRotation,
            Vector3 localScale, Color color, bool emissive)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            // Match the runtime - decorative only, walkable.
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            ApplyMaterial(go, color, emissive);
        }

        private static void EnsureChild(Transform parent, string name, System.Func<GameObject> factory)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i) != null && parent.GetChild(i).name == name)
                    return;
            }

            var go = factory();
            if (go == null) return;
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: true);
        }

        private static GameObject FindRoot(Scene scene, string objectName)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && roots[i].name == objectName)
                    return roots[i];
            }
            return null;
        }

        private static void ApplyMaterial(GameObject go, Color color, bool emissive)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color, name = $"{go.name} Material" };
            if (emissive && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.4f);
            }
            renderer.sharedMaterial = mat;
        }

        private static int CountPrefabs(TestWorldDisplaySet displaySet)
        {
            var count = 0;
            foreach (var section in displaySet.Sections)
            {
                if (section?.prefabs == null) continue;
                for (var i = 0; i < section.prefabs.Length; i++)
                    if (section.prefabs[i] != null) count++;
            }
            return count;
        }
    }
}
#endif
