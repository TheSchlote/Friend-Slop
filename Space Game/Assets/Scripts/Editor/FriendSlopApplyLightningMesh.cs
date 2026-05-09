#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    // One-shot editor menu that swaps ElectricOrb.prefab's authored mesh from Unity's
    // built-in sphere primitive to the mesh imported from Lightning anom.blend. The
    // mesh fileID inside a .blend is generated as a stable hash on import and isn't
    // hand-authorable, so this resolves it at editor time and rewrites the prefab.
    // Idempotent: re-running is harmless; if the prefab already points at the .blend
    // mesh it just re-saves the same reference.
    public static class FriendSlopApplyLightningMesh
    {
        private const string BlendPath = "Assets/Prefabs/Anomalies/Lightning anom.blend";
        private const string PrefabPath = "Assets/Prefabs/Anomalies/ElectricOrb.prefab";
        private const string MaterialPath = "Assets/Materials/LootRarity/AnomalyLightning.mat";

        [MenuItem("Tools/Friend Slop/Apply Lightning Mesh")]
        public static void Run()
        {
            var mesh = ResolveBlendMesh(BlendPath);
            if (mesh == null)
            {
                EditorUtility.DisplayDialog(
                    "Friend Slop",
                    $"Could not find a Mesh inside '{BlendPath}'. Make sure the .blend is imported " +
                    "(open the project in Unity once, then re-run this menu).",
                    "OK");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Friend Slop", $"Prefab not found at '{PrefabPath}'.", "OK");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var filter = contents.GetComponentInChildren<MeshFilter>(true);
                if (filter == null)
                {
                    EditorUtility.DisplayDialog(
                        "Friend Slop",
                        $"'{PrefabPath}' has no MeshFilter; nothing to swap.",
                        "OK");
                    return;
                }

                if (filter.sharedMesh != mesh)
                {
                    filter.sharedMesh = mesh;
                    EditorUtility.SetDirty(filter);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Friend Slop",
                $"ElectricOrb prefab now uses mesh '{mesh.name}' from {BlendPath}.\n\n" +
                "If the new mesh looks oversized or undersized, adjust ElectricOrb's transform " +
                "scale (currently 0.4). The orb material is unchanged - tweak " +
                $"'{MaterialPath}' if you want a different lightning shader.",
                "OK");
        }

        private static Mesh ResolveBlendMesh(string blendPath)
        {
            // Sub-asset scan: a .blend usually exposes the GameObject as the main asset and
            // the mesh as a sub-asset. LoadAllAssetRepresentationsAtPath returns those subs.
            var representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(blendPath);
            foreach (var asset in representations)
            {
                if (asset is Mesh m) return m;
            }

            // Fallback: dig the mesh out of the imported GameObject hierarchy if the sub-asset
            // path returned nothing (older import settings, single-mesh .blends, etc.).
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(blendPath);
            if (root == null) return null;
            var filter = root.GetComponentInChildren<MeshFilter>(true);
            return filter != null ? filter.sharedMesh : null;
        }
    }
}
#endif
