using System.Collections.Generic;
using System.IO;
using FriendSlop.Interiors.Blocks;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    // Editor menu: scans Assets/LowPolyInterior/Prefabs/Walls and
    // Assets/LowPolyInterior/Models/Floor for prefabs / model assets matching
    // each BlockKind and populates the BlockPrefabCatalog at the canonical
    // path. Designer never has to drag prefabs by hand.
    //
    // Idempotent — preserves any StyleTag overrides on existing entries by
    // GUID. Variants that no longer exist on disk are removed.
    internal static class BlockPrefabCatalogScanner
    {
        private const string CatalogPath = "Assets/Interiors/BlockPrefabCatalog.asset";
        private const string WallsFolder = "Assets/LowPolyInterior/Prefabs/Walls";

        // Hand-classified from the LowPolyInterior pack (verified by FBX mesh
        // complexity — plain wall meshes are ~28 KB, windowed ~102 KB, doored/
        // detailed ~105 KB). Names without a leading prefix that we still want
        // to bucket explicitly.
        private static readonly HashSet<string> PlainWalls = new()
            { "Wall_01", "Wall_02", "Wall_09" };
        private static readonly HashSet<string> WindowWalls = new()
            { "Wall_03", "Wall_04", "Wall_05", "Wall_06", "Wall_07", "Wall_08" };
        // Wall_10..13 are doored / heavily-detailed — left out of both pools so
        // they don't surprise the designer. Use the dedicated Door_* set.
        private const string FloorFolder = "Assets/LowPolyInterior/Prefabs/Floor";

        [MenuItem("Tools/Friend Slop/Interiors/Repair Block Catalog")]
        public static void Repair()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BlockPrefabCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BlockPrefabCatalog>();
                var dir = Path.GetDirectoryName(CatalogPath);
                if (!AssetDatabase.IsValidFolder(dir))
                    Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            // Snapshot existing style overrides so re-running the menu doesn't
            // wipe authored intent.
            var existingStyleByPath = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var v in catalog.Variants)
            {
                if (v?.Prefab == null) continue;
                var path = AssetDatabase.GetAssetPath(v.Prefab);
                if (!string.IsNullOrEmpty(path)) existingStyleByPath[path] = v.StyleTag ?? "";
            }

            var fresh = new System.Collections.Generic.List<BlockPrefabCatalog.Variant>();

            // Walls: only plain Wall_*.prefab → Wall, Door_*.prefab → Door.
            // The WallFloor*.prefab variants in this pack are window-frame walls
            // and corner pieces, not plain walls — they were polluting the Wall
            // pool with windowed / corner geometry, so we skip them here.
            // (To add a Window pool later, scan a separate Window_* prefix or
            // tag specific WallFloor variants by hand.)
            ScanFolder(WallsFolder, "*.prefab", (path, asset) =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var styleTag = existingStyleByPath.TryGetValue(path, out var s) ? s : "";
                if (name.StartsWith("Door_"))
                {
                    fresh.Add(new BlockPrefabCatalog.Variant
                    { Prefab = asset, Kind = BlockKind.Door, StyleTag = styleTag });
                }
                else if (name.StartsWith("Window_") || WindowWalls.Contains(name))
                {
                    // Dedicated window pool — windowed wall meshes only.
                    fresh.Add(new BlockPrefabCatalog.Variant
                    { Prefab = asset, Kind = BlockKind.Window, StyleTag = styleTag });
                }
                else if (PlainWalls.Contains(name))
                {
                    // Clean wall pool — plain wall meshes only.
                    fresh.Add(new BlockPrefabCatalog.Variant
                    { Prefab = asset, Kind = BlockKind.Wall, StyleTag = styleTag });
                }
                // else: Wall_10..13 (doored/detailed), WallFloor, Corner,
                // Stairs, Railing — intentionally excluded.
            });

            // Floors: prefer the Prefab versions (proper Unity prefabs with all
            // import settings baked in) over raw FBX models.
            ScanFolder(FloorFolder, "*.prefab", (path, asset) =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("Floor_")) return;
                fresh.Add(new BlockPrefabCatalog.Variant
                {
                    Prefab   = asset,
                    Kind     = BlockKind.Floor,
                    StyleTag = existingStyleByPath.TryGetValue(path, out var s) ? s : "",
                });
            });

            catalog.Variants = fresh;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Selection.activeObject = catalog;
            Debug.Log($"[BlockCatalog] Repaired: {catalog.Variants.Count} variants (Walls {Count(catalog, BlockKind.Wall)}, Doors {Count(catalog, BlockKind.Door)}, Floors {Count(catalog, BlockKind.Floor)}).");
        }

        private static int Count(BlockPrefabCatalog catalog, BlockKind kind)
        {
            int n = 0;
            foreach (var v in catalog.Variants) if (v != null && v.Kind == kind) n++;
            return n;
        }

        private static void ScanFolder(string folder, string pattern,
                                        System.Action<string, GameObject> visit)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[BlockCatalog] Folder not found: {folder}");
                return;
            }
            var guids = AssetDatabase.FindAssets("t:GameObject", new[] { folder });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(pattern.TrimStart('*'))) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                visit(path, asset);
            }
        }
    }
}
