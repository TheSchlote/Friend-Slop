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
            // Always rebuild — the menu is "Add Test Building" and the user expects a fresh
            // known-good state every time. Idempotency by name caused stale layouts to stick.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != TestBuildingName) continue;
                Object.DestroyImmediate(root);
                Debug.Log("[Friend Slop] Removed previous test building.");
                break;
            }

            var smallDef = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
                $"{InteriorBuildingFolder}/Building_Small.asset");

            if (smallDef == null)
            {
                Debug.LogWarning("[Friend Slop] Interior assets missing — run 'Repair Interior Assets' first.");
                return false;
            }

            var go = new GameObject(TestBuildingName);
            SceneManager.MoveGameObjectToScene(go, scene);

            const float shellSize = 8f;
            const float halfShell = shellSize * 0.5f;

            // Place on the planet surface, prioritising the entry door — we shift the pivot
            // so the door's bottom lands on the actual surface in the door's direction.
            // Otherwise on a small planet (R=18) the building's centre sits flush but the
            // door floats a metre off the ground.
            var world = Object.FindFirstObjectByType<SphereWorld>(FindObjectsInactive.Include);
            if (world != null)
            {
                var surfaceDir = new Vector3(0.3f, 0.9f, 0.2f).normalized;
                var rotation = world.GetSurfaceRotation(surfaceDir, Vector3.forward);
                var centerSurface = world.GetSurfacePoint(surfaceDir, 0f);

                // Where would the door bottom land if pivot was at centerSurface?
                var doorLocalOffset = new Vector3(0f, 0f, halfShell);
                var doorWorld = centerSurface + rotation * doorLocalOffset;

                // Sample the actual surface in the door's direction (picks up terrain too).
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

            // Placeholder exterior shell, centred on the pivot, sitting on the surface (Y=0..8).
            // Keep its BoxCollider so the player can't walk through walls.
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "ExteriorShell";
            shell.transform.SetParent(go.transform, false);
            shell.transform.localPosition = new Vector3(0f, shellSize * 0.5f, 0f);
            shell.transform.localScale    = new Vector3(shellSize, shellSize, shellSize);

            // Door visual on the front (+Z) face — same plane as the interactable collider.
            var doorVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorVisual.name = "DoorVisual";
            doorVisual.transform.SetParent(go.transform, false);
            doorVisual.transform.localPosition = new Vector3(0f, 1.25f, shellSize * 0.5f + 0.06f);
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
            col.center = new Vector3(0f, 1.25f, shellSize * 0.5f + 0.25f);

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
