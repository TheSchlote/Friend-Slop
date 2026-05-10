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

            var changed = EnsureTestBuildingInScene(scene);
            if (changed)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Friend Slop] Test building added to Hills and Valleys scene.");
            }
            else
            {
                Debug.Log("[Friend Slop] Test building already present in Hills and Valleys scene.");
            }
        }

        private static bool EnsureTestBuildingInScene(Scene scene)
        {
            // Idempotent: bail if building is already in the scene.
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == TestBuildingName) return false;

            var smallDef = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
                $"{InteriorBuildingFolder}/Building_Small.asset");

            if (smallDef == null)
            {
                Debug.LogWarning("[Friend Slop] Interior assets missing — run 'Repair Interior Assets' first.");
                return false;
            }

            var go = new GameObject(TestBuildingName);
            SceneManager.MoveGameObjectToScene(go, scene);

            // Place on the planet surface.
            var world = Object.FindFirstObjectByType<SphereWorld>(FindObjectsInactive.Include);
            if (world != null)
            {
                var surfaceDir = new Vector3(0.3f, 0.9f, 0.2f).normalized;
                go.transform.position = world.GetSurfacePoint(surfaceDir, 0.5f);
                go.transform.rotation = world.GetSurfaceRotation(surfaceDir, Vector3.forward);
                go.AddComponent<PlanetSurfaceAnchor>();
            }
            else
            {
                go.transform.position = new Vector3(10f, 1f, 0f);
            }

            // Placeholder exterior so the building is visible in-editor.
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "ExteriorShell";
            shell.transform.SetParent(go.transform, false);
            shell.transform.localPosition = new Vector3(4f, 4f, 4f);
            shell.transform.localScale    = new Vector3(8f, 8f, 8f);
            Object.DestroyImmediate(shell.GetComponent<Collider>());

            // Non-trigger so PlayerInteractor's SphereCast (QueryTriggerInteraction.Ignore) detects it.
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = false;
            col.size   = new Vector3(2f, 3f, 0.3f);
            col.center = new Vector3(0f, 1.5f, 0f);

            // NetworkObject required because InteriorEntrance is a NetworkBehaviour.
            go.AddComponent<NetworkObject>();

            var entrance = go.AddComponent<InteriorEntrance>();
            var so = new SerializedObject(entrance);
            so.FindProperty("definition").objectReferenceValue = smallDef;
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
