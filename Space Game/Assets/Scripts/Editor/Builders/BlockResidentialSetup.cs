using System.IO;
using FriendSlop.Interiors;
using FriendSlop.Interiors.Blocks;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    // One-click bootstrap of the residential block system. Runs three steps in
    // sequence:
    //   1. Repair the BlockPrefabCatalog (scans LowPolyInterior/Walls + Floor).
    //   2. Create an empty Residential_Default.asset BlockBlueprintAsset if it
    //      doesn't already exist.
    //   3. Wire both onto Building_Residential.asset's new blockBlueprint /
    //      blockCatalog fields.
    //
    // Idempotent: re-running re-scans the catalog, leaves the blueprint as-is
    // (so edits made via F3 survive), and only re-wires if the fields were null.
    internal static class BlockResidentialSetup
    {
        private const string CatalogPath           = "Assets/Interiors/BlockPrefabCatalog.asset";
        private const string DefaultBlueprintPath  = "Assets/Interiors/Blueprints/Residential_Default.asset";
        private const string ResidentialBuildingPath = "Assets/Interiors/Buildings/Building_Residential.asset";

        [MenuItem("Tools/Friend Slop/Interiors/Setup Residential Block System")]
        public static void Setup()
        {
            // Step 1 — catalog.
            BlockPrefabCatalogScanner.Repair();
            var catalog = AssetDatabase.LoadAssetAtPath<BlockPrefabCatalog>(CatalogPath);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("Friend Slop", "Catalog repair failed — see Console.", "OK");
                return;
            }

            // Step 2 — default residential block blueprint.
            EnsureFolder("Assets/Interiors/Blueprints");
            var bp = AssetDatabase.LoadAssetAtPath<BlockBlueprintAsset>(DefaultBlueprintPath);
            if (bp == null)
            {
                bp = ScriptableObject.CreateInstance<BlockBlueprintAsset>();
                bp.DisplayName       = "Residential Default";
                AssetDatabase.CreateAsset(bp, DefaultBlueprintPath);
                Debug.Log($"[ResidentialSetup] Created {DefaultBlueprintPath}");
            }

            // Size the grid to the wall prefab's native dimensions so walls are
            // never scaled (keeps window textures crisp) and floors get scaled
            // up to match. Always re-measure on setup so swapping the wall pool
            // keeps the grid aligned.
            MeasureAndApplyGridFromWall(catalog, bp);

            // Seed a 5×5 starter floor patch at floor 0 so a player walking
            // through the entrance lands on something instead of falling into
            // the void. Only seeds when the blueprint has no Floor blocks yet,
            // so re-running this menu won't keep stacking starter tiles.
            if (!HasAnyFloorTiles(bp))
            {
                for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    bp.Blocks.Add(new BlockEntry
                    {
                        Cell  = new Vector3Int(x, 0, z),
                        Kind  = BlockKind.Floor,
                        Tags  = new[] { "default" },
                        Label = "Default",
                    });
                }
                EditorUtility.SetDirty(bp);
                Debug.Log($"[ResidentialSetup] Seeded {DefaultBlueprintPath} with a 5×5 starter floor patch.");
            }
            AssetDatabase.SaveAssets();

            // Step 3 — wire onto Building_Residential.
            var residential = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(ResidentialBuildingPath);
            if (residential == null)
            {
                EditorUtility.DisplayDialog("Friend Slop",
                    $"Couldn't find {ResidentialBuildingPath} — skipping wiring.", "OK");
                return;
            }
            var so = new SerializedObject(residential);
            var bpProp  = so.FindProperty("blockBlueprint");
            var catProp = so.FindProperty("blockCatalog");
            bool changed = false;
            if (bpProp != null && bpProp.objectReferenceValue == null)
            { bpProp.objectReferenceValue = bp; changed = true; }
            if (catProp != null && catProp.objectReferenceValue == null)
            { catProp.objectReferenceValue = catalog; changed = true; }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(residential);
            AssetDatabase.SaveAssets();

            Selection.activeObject = residential;
            EditorUtility.DisplayDialog("Friend Slop",
                $"Residential block system ready.\n\n" +
                $"Catalog: {catalog.Variants.Count} variants\n" +
                $"Blueprint: {DefaultBlueprintPath}\n" +
                $"Wired onto: {ResidentialBuildingPath}\n\n" +
                "Press Play, walk into the residential building, press F3 to edit.",
                "OK");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            var name = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static bool HasAnyFloorTiles(BlockBlueprintAsset bp)
        {
            if (bp?.Blocks == null) return false;
            foreach (var b in bp.Blocks) if (b.Kind == BlockKind.Floor) return true;
            return false;
        }

        // Instantiate a sample Wall variant, measure its combined renderer
        // bounds, and write CellMetres = wall width / WallHeightMetres = wall
        // height onto the blueprint. With these, the materialiser's "don't
        // scale walls" rule means a native wall spans exactly one cell.
        private static void MeasureAndApplyGridFromWall(BlockPrefabCatalog catalog,
                                                        BlockBlueprintAsset bp)
        {
            GameObject wallPrefab = null;
            foreach (var v in catalog.Variants)
                if (v != null && v.Kind == BlockKind.Wall && v.Prefab != null)
                { wallPrefab = v.Prefab; break; }
            if (wallPrefab == null)
            {
                Debug.LogWarning("[ResidentialSetup] No Wall variant in catalog — keeping existing grid size.");
                return;
            }

            var temp = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab);
            try
            {
                temp.transform.position = Vector3.zero;
                temp.transform.rotation = Quaternion.identity;
                temp.transform.localScale = Vector3.one;
                // Aggregate MeshFilter.sharedMesh bounds transformed into the
                // prefab root's local space. sharedMesh.bounds is always valid
                // (no render needed), unlike Renderer.bounds for an object that
                // hasn't been drawn yet in an editor menu.
                var filters = temp.GetComponentsInChildren<MeshFilter>(true);
                bool any = false;
                Vector3 min = Vector3.zero, max = Vector3.zero;
                foreach (var mf in filters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mb = mf.sharedMesh.bounds;
                    var t  = mf.transform;
                    Vector3[] corners =
                    {
                        new(mb.min.x, mb.min.y, mb.min.z), new(mb.max.x, mb.min.y, mb.min.z),
                        new(mb.min.x, mb.max.y, mb.min.z), new(mb.max.x, mb.max.y, mb.min.z),
                        new(mb.min.x, mb.min.y, mb.max.z), new(mb.max.x, mb.min.y, mb.max.z),
                        new(mb.min.x, mb.max.y, mb.max.z), new(mb.max.x, mb.max.y, mb.max.z),
                    };
                    foreach (var cn in corners)
                    {
                        var p = temp.transform.InverseTransformPoint(t.TransformPoint(cn));
                        if (!any) { min = max = p; any = true; }
                        else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    }
                }
                if (!any)
                {
                    Debug.LogWarning("[ResidentialSetup] Wall prefab has no meshes — keeping existing grid size.");
                    return;
                }
                var size = max - min;
                float width  = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z));
                float height = Mathf.Abs(size.y);
                if (width  > 0.01f) bp.CellMetres       = Mathf.Round(width  * 100f) / 100f;
                if (height > 0.01f) bp.WallHeightMetres = Mathf.Round(height * 100f) / 100f;
                EditorUtility.SetDirty(bp);
                Debug.Log($"[ResidentialSetup] Grid sized to wall '{wallPrefab.name}': " +
                          $"CellMetres={bp.CellMetres}, WallHeightMetres={bp.WallHeightMetres}.");
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }
    }
}
