using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FriendSlop.Interiors;
using FriendSlop.Interiors.Blueprints;

namespace FriendSlop.Editor
{
    // One-time scene setup for the blueprint preview entrance. Drops a permanent
    // BlueprintEntrance into Hills and Valleys near the launchpad — players walk
    // up to it, press E, and load the Building_Interior scene materialised from
    // the blueprint instead of the procedural generator.
    // Tools → Friend Slop → Interiors → Setup Blueprint Entrance (HV).
    internal static class BlueprintTestBuildingSetup
    {
        private const string ScenePath  = "Assets/Scenes/Planet_HillsAndValleys.unity";
        private const string EntranceName = "BlueprintEntrance";
        private const string ResidentialBuildingPath = "Assets/Interiors/Buildings/Building_Residential.asset";

        [MenuItem("Tools/Friend Slop/Interiors/Setup Blueprint Entrance (HV)")]
        public static void SetupInHV()
        {
            // Open HV scene if not already open.
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var scene = EditorSceneManager.GetActiveScene();

            // Find or create the entrance GameObject. Carries a Collider (for the
            // E-press interaction trigger) and the BlueprintEntrance component.
            var existing = GameObject.Find(EntranceName);
            GameObject entranceGo;
            BlueprintEntrance entrance;
            if (existing != null)
            {
                entranceGo = existing;
                entrance = existing.GetComponent<BlueprintEntrance>();
                if (entrance == null) entrance = existing.AddComponent<BlueprintEntrance>();
            }
            else
            {
                entranceGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                entranceGo.name = EntranceName;
                // Visual marker — small bluish slab so designers can spot it on the
                // planet. Collider is the BoxCollider added by CreatePrimitive.
                entranceGo.transform.localScale = new Vector3(2f, 3f, 0.4f);
                var renderer = entranceGo.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                    {
                        color = new Color(0.30f, 0.55f, 1f)
                    };
                }
                SceneManager.MoveGameObjectToScene(entranceGo, scene);
                // BlueprintEntrance is a NetworkBehaviour and needs a NetworkObject
                // sibling component. Add it BEFORE the entrance so Unity doesn't
                // prompt the user with the "missing NetworkObject" dialog.
                entranceGo.AddComponent<NetworkObject>();
                entrance = entranceGo.AddComponent<BlueprintEntrance>();
            }
            // If the user added BlueprintEntrance manually first and is re-running
            // this menu, ensure NetworkObject exists too.
            if (entranceGo.GetComponent<NetworkObject>() == null)
                entranceGo.AddComponent<NetworkObject>();

            // The launchpad is runtime-spawned (CreateLaunchpad puts it at
            // sphereCenter + Vector3.up * radius, see FriendSlopSceneBuilder.cs:438)
            // so we can't find it at edit time. Compute the launchpad's would-be
            // spawn position deterministically from the SphereWorld and offset from
            // there. If no SphereWorld either, fall back to world origin.
            var sphereWorld = Object.FindFirstObjectByType<FriendSlop.Core.SphereWorld>();
            if (sphereWorld != null)
            {
                Vector3 launchpadPos = sphereWorld.Center + Vector3.up * sphereWorld.Radius;
                // 30 m east of the launchpad. East is tangent to the surface at the
                // launchpad's north-pole position, so it's a clean walk away.
                Vector3 offset = launchpadPos + Vector3.right * 30f;
                Vector3 fromCentre = (offset - sphereWorld.Center).normalized;
                entranceGo.transform.position = sphereWorld.Center + fromCentre * sphereWorld.Radius;
                entranceGo.transform.rotation = Quaternion.FromToRotation(Vector3.up, fromCentre);
            }
            else
            {
                Debug.LogWarning("[BlueprintTestBuildingSetup] No SphereWorld in scene; placing entrance at world origin.");
                entranceGo.transform.position = Vector3.zero;
                entranceGo.transform.rotation = Quaternion.identity;
            }

            // Wire the residential building def + first available blueprint as defaults.
            var entranceSo = new SerializedObject(entrance);
            if (entranceSo.FindProperty("definition").objectReferenceValue == null)
            {
                var defAsset = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(ResidentialBuildingPath);
                if (defAsset != null) entranceSo.FindProperty("definition").objectReferenceValue = defAsset;
            }
            if (entranceSo.FindProperty("blueprint").objectReferenceValue == null)
            {
                var guids = AssetDatabase.FindAssets("t:BlueprintAsset",
                    new[] { BlueprintEditorController.BlueprintFolder });
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var bp = AssetDatabase.LoadAssetAtPath<BlueprintAsset>(path);
                    if (bp != null) entranceSo.FindProperty("blueprint").objectReferenceValue = bp;
                }
            }
            entranceSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(entranceGo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = entranceGo;
            EditorUtility.DisplayDialog("Friend Slop",
                "BlueprintEntrance set up in Hills and Valleys. Walk up to the blue " +
                "slab near the launchpad and press E to enter the blueprint preview. " +
                "By default it follows the in-game editor's currently-loaded blueprint.",
                "OK");
        }
    }
}
