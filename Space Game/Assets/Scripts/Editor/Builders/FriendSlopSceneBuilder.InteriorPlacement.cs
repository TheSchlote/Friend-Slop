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

        private static bool EnsureBuildingInScene(Scene scene, string buildingName,
            string buildingDefName, Vector3 surfaceDir)
        {
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
                return false;
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
            return true;
        }
    }
}
#endif
