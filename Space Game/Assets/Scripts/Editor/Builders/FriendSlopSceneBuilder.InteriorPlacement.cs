#if UNITY_EDITOR
using FriendSlop.Core;
using FriendSlop.Interiors;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private const string HillsAndValleysScenePath = "Assets/Scenes/Planet_HillsAndValleys.unity";
        private const string TestBuildingName = "TestBuilding_Interior";
        private const string MultifloorBuildingName = "TestBuilding_Multifloor";

        [MenuItem("Tools/Friend Slop/Interiors/Add Test Building to Hills and Valleys")]
        public static void AddTestBuildingToHillsAndValleys()
        {
            RepairInteriorAssets();

            var scene = EditorSceneManager.OpenScene(HillsAndValleysScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Friend Slop] Could not open scene at {HillsAndValleysScenePath}");
                return;
            }

            var smallChanged = EnsureBuildingInScene(scene, TestBuildingName,
                "Building_Small", new Vector3(0.3f, 0.9f, 0.2f).normalized);
            var mfChanged = EnsureBuildingInScene(scene, MultifloorBuildingName,
                "Building_Multifloor", new Vector3(-0.4f, 0.8f, -0.3f).normalized);

            if (smallChanged || mfChanged)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Friend Slop] Test buildings added to Hills and Valleys scene.");
            }
        }

<<<<<<< HEAD
        private static bool EnsureBuildingInScene(Scene scene, string buildingName,
            string buildingDefName, Vector3 surfaceDir)
        {
=======
        [MenuItem("Tools/Friend Slop/Interiors/Add Type Test Buildings to Hills and Valleys")]
        public static void AddTypeTestBuildingsToHillsAndValleys()
        {
            RepairInteriorAssets();

            var scene = EditorSceneManager.OpenScene(HillsAndValleysScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[Friend Slop] Could not open scene at {HillsAndValleysScenePath}");
                return;
            }

            // Spread the four test buildings around the planet so they don't overlap.
            var residentialChanged = EnsureBuildingInScene(scene, "TestBuilding_Residential",
                "Building_Residential", new Vector3( 0.9f,  0.3f,  0.2f).normalized);
            var officeChanged = EnsureBuildingInScene(scene, "TestBuilding_Office",
                "Building_Office",      new Vector3( 0.0f,  0.5f,  0.9f).normalized);
            var factoryChanged = EnsureBuildingInScene(scene, "TestBuilding_Factory",
                "Building_Factory",     new Vector3(-0.9f,  0.3f, -0.1f).normalized);
            var selectorChanged = EnsureSelectorTestBuilding(scene);

            if (residentialChanged || officeChanged || factoryChanged || selectorChanged)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Friend Slop] Type test buildings (Residential / Office / Factory / TypeSelector) added to Hills and Valleys scene.");
            }
        }

        private static bool EnsureBuildingInScene(Scene scene, string buildingName,
            string buildingDefName, Vector3 surfaceDir)
        {
            return CreateTestBuilding(scene, buildingName, buildingDefName, surfaceDir) != null;
        }

        // Shared placement logic. Returns the entrance component so the caller can wire
        // additional features (e.g. attaching a type selector for the test pillar).
        private static InteriorEntrance CreateTestBuilding(Scene scene, string buildingName,
            string buildingDefName, Vector3 surfaceDir)
        {
>>>>>>> origin/interiors-changes
            // Always rebuild — the menu expects a fresh known-good state every time.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != buildingName) continue;
                Object.DestroyImmediate(root);
                Debug.Log($"[Friend Slop] Removed previous '{buildingName}'.");
                break;
            }

            var def = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
                $"{InteriorBuildingFolder}/{buildingDefName}.asset");

            if (def == null)
            {
                Debug.LogWarning($"[Friend Slop] Building definition '{buildingDefName}' missing — run 'Repair Interior Assets' first.");
<<<<<<< HEAD
                return false;
=======
                return null;
>>>>>>> origin/interiors-changes
            }

            var go = new GameObject(buildingName);
            SceneManager.MoveGameObjectToScene(go, scene);

            const float shellWidth = 8f;       // X / Z extent (matches one grid cell)
            const float halfWidth  = shellWidth * 0.5f;
            float shellHeight      = def.MaxFloors * def.FloorHeightMeters;

            // Place on the planet surface, prioritising the entry door — we shift the pivot
            // so the door's bottom lands on the actual surface in the door's direction.
            var world = Object.FindFirstObjectByType<SphereWorld>(FindObjectsInactive.Include);
            if (world != null)
            {
                var rotation = world.GetSurfaceRotation(surfaceDir, Vector3.forward);
                var centerSurface = world.GetSurfacePoint(surfaceDir, 0f);

                var doorLocalOffset = new Vector3(0f, 0f, halfWidth);
                var doorWorld = centerSurface + rotation * doorLocalOffset;

                var doorDir = (doorWorld - world.Center).normalized;
                var doorSurface = world.GetSurfacePoint(doorDir, 0f);

                go.transform.position = centerSurface + (doorSurface - doorWorld);
                go.transform.rotation = rotation;
                go.AddComponent<PlanetSurfaceAnchor>();
            }
            else
            {
                go.transform.position = new Vector3(10f, 1f, 0f);
            }

            // Placeholder exterior shell. Height scales with floor count.
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "ExteriorShell";
            shell.transform.SetParent(go.transform, false);
            shell.transform.localPosition = new Vector3(0f, shellHeight * 0.5f, 0f);
            shell.transform.localScale    = new Vector3(shellWidth, shellHeight, shellWidth);

            // Door visual on the front (+Z) face — same plane as the interactable collider.
            var doorVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorVisual.name = "DoorVisual";
            doorVisual.transform.SetParent(go.transform, false);
            doorVisual.transform.localPosition = new Vector3(0f, 1.25f, halfWidth + 0.06f);
            doorVisual.transform.localScale    = new Vector3(1.6f, 2.5f, 0.1f);
            Object.DestroyImmediate(doorVisual.GetComponent<Collider>());
            var renderer = doorVisual.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // Saddle brown — visible against grey shell and green hills.
                var brown = new Color(0.55f, 0.3f, 0.1f);
                var src = renderer.sharedMaterial != null
                    ? renderer.sharedMaterial
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                var mat = new Material(src) { color = brown };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", brown);
                renderer.sharedMaterial = mat;
            }

            // Non-trigger collider just outside the shell so SphereCast hits it before the
            // shell's wall. (0.25 m clear of the shell face.)
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;
            col.size   = new Vector3(1.6f, 2.5f, 0.2f);
            col.center = new Vector3(0f, 1.25f, halfWidth + 0.25f);

            // NetworkObject required because InteriorEntrance is a NetworkBehaviour.
            go.AddComponent<NetworkObject>();

            var entrance = go.AddComponent<InteriorEntrance>();
            var so = new SerializedObject(entrance);
            so.FindProperty("definition").objectReferenceValue = def;
            so.FindProperty("interiorScenePath").stringValue   = InteriorScenePath;
            so.ApplyModifiedPropertiesWithoutUndo();

            // One loading-screen canvas per scene is enough (static event broadcasts to all).
            BuildLoadingScreenCanvas(go.transform);

            EditorUtility.SetDirty(go);
<<<<<<< HEAD
            return true;
        }
=======
            return entrance;
        }

        // Adds a 4th test building with a type-selector pillar next to it. Pressing E on the
        // pillar cycles through every BuildingDefinition in the InteriorCatalog; the entrance
        // reads its definition from the selector at entry time. Placed near the launchpad so
        // it's easy to find from the player spawn.
        private static bool EnsureSelectorTestBuilding(Scene scene)
        {
            const string buildingName = "TestBuilding_TypeSelector";

            var surfaceDir = ComputeSelectorSurfaceDir(scene);

            var entrance = CreateTestBuilding(scene, buildingName, "Building_Residential", surfaceDir);
            if (entrance == null) return false;

            // Pillar GameObject is a child of the building root so it inherits the same
            // surface rotation, and cleans up automatically when the building is rebuilt.
            var pillar = BuildSelectorPillar(entrance.transform);

            // Wire entrance.typeSelector -> selector component on the pillar.
            var selector = pillar.GetComponent<InteriorTypeSelector>();
            var so = new SerializedObject(entrance);
            so.FindProperty("typeSelector").objectReferenceValue = selector;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(entrance);
            return true;
        }

        // Looks up the launchpad GameObject in the scene and returns a surface direction
        // a few metres to its side, so the test building sits next to the launchpad but
        // not on top of it. Falls back to a default direction if no launchpad is found.
        private static Vector3 ComputeSelectorSurfaceDir(Scene scene)
        {
            var fallback = new Vector3(0.5f, -0.4f, 0.7f).normalized;

            var world = Object.FindFirstObjectByType<SphereWorld>(FindObjectsInactive.Include);
            if (world == null) return fallback;

            LaunchpadZone pad = null;
            foreach (var z in Object.FindObjectsByType<LaunchpadZone>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (z.gameObject.scene == scene) { pad = z; break; }
            }
            if (pad == null) return fallback;

            // Surface direction the launchpad sits on.
            var padDir = (pad.transform.position - world.Center).normalized;

            // Pick any tangent vector on the surface plane and offset the building roughly
            // ~12 m / world.Radius radians along it — far enough to be off the pad, close
            // enough that the player spawned at the pad can see it.
            var tangent = Vector3.Cross(padDir, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(padDir, Vector3.right);
            tangent.Normalize();

            float angularOffset = 12f / Mathf.Max(1f, world.Radius);   // ~12 m of arc
            return Quaternion.AngleAxis(angularOffset * Mathf.Rad2Deg, tangent) * padDir;
        }

        private static GameObject BuildSelectorPillar(Transform buildingRoot)
        {
            const float halfWidth = 4f;

            // Selector pillar — 0.8 m × 1.5 m × 0.8 m, placed clear of the shell's east face
            // and 1.5 m in front of the building so the player walks past it toward the door.
            var pillar = new GameObject("TypeSelectorPillar");
            pillar.transform.SetParent(buildingRoot, false);
            pillar.transform.localPosition = new Vector3(halfWidth + 1.5f, 0f, halfWidth + 1.5f);

            var col = pillar.AddComponent<BoxCollider>();
            col.size   = new Vector3(0.8f, 1.5f, 0.8f);
            col.center = new Vector3(0f, 0.75f, 0f);
            col.isTrigger = false;

            pillar.AddComponent<NetworkObject>();
            var selector = pillar.AddComponent<InteriorTypeSelector>();

            // Visible pillar mesh (no collider — the one above on the root handles SphereCast).
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "PillarMesh";
            mesh.transform.SetParent(pillar.transform, false);
            mesh.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            mesh.transform.localScale    = new Vector3(0.8f, 1.5f, 0.8f);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());

            // Worldspace text above the pillar. Updated at runtime by InteriorTypeSelector.
            // TextMesh faces local +Z; rotate 180° so the text reads correctly when the
            // player approaches the pillar from the entrance side of the building.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(pillar.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text          = "Current: ...";
            tm.fontSize      = 64;
            tm.characterSize = 0.08f;
            tm.anchor        = TextAnchor.MiddleCenter;
            tm.alignment     = TextAlignment.Center;
            tm.color         = Color.white;

            var so = new SerializedObject(selector);
            so.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InteriorCatalog>(
                $"{InteriorAssetFolder}/InteriorCatalog.asset");
            so.FindProperty("label").objectReferenceValue   = tm;
            so.ApplyModifiedPropertiesWithoutUndo();

            return pillar;
        }
>>>>>>> origin/interiors-changes
    }
}
#endif
