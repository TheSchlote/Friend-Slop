using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FriendSlop.Interiors.Blocks
{
    // Save / load / rename / new operations for BlockBlueprintAsset. Editor-only
    // (uses AssetDatabase) — the block editors run in editor Play Mode, which is
    // the vibe-coded workflow, so this is fine. Outside the editor these are
    // no-ops / empty lists.
    public static class BlockBlueprintIO
    {
        public const string Folder = "Assets/Interiors/Blueprints";

        public static void Save(BlockBlueprintAsset bp)
        {
            #if UNITY_EDITOR
            if (bp == null) return;
            EditorUtility.SetDirty(bp);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BlockIO] Saved '{bp.DisplayName}'.");
            #endif
        }

        public static void Rename(BlockBlueprintAsset bp, string newName)
        {
            #if UNITY_EDITOR
            if (bp == null || string.IsNullOrWhiteSpace(newName)) return;
            newName = newName.Trim();
            bp.DisplayName = newName;
            var path = AssetDatabase.GetAssetPath(bp);
            var newPath = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{newName}.asset");
            AssetDatabase.RenameAsset(path, System.IO.Path.GetFileNameWithoutExtension(newPath));
            EditorUtility.SetDirty(bp);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BlockIO] Renamed → '{newName}'.");
            #endif
        }

        // All BlockBlueprintAssets in the project, for the load picker.
        public static List<BlockBlueprintAsset> ListAll()
        {
            var list = new List<BlockBlueprintAsset>();
            #if UNITY_EDITOR
            foreach (var g in AssetDatabase.FindAssets("t:BlockBlueprintAsset"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var a = AssetDatabase.LoadAssetAtPath<BlockBlueprintAsset>(p);
                if (a != null) list.Add(a);
            }
            list.Sort((x, y) => string.CompareOrdinal(
                x.DisplayName ?? x.name, y.DisplayName ?? y.name));
            #endif
            return list;
        }

        // Create a fresh blueprint, copying grid sizing from `template` (so it
        // stays aligned to the same wall pack) and seeding a small starter
        // floor patch so the player has somewhere to stand on entry.
        public static BlockBlueprintAsset CreateNew(string displayName, BlockBlueprintAsset template)
        {
            #if UNITY_EDITOR
            EnsureFolder();
            var bp = ScriptableObject.CreateInstance<BlockBlueprintAsset>();
            bp.DisplayName      = string.IsNullOrWhiteSpace(displayName) ? "Untitled" : displayName.Trim();
            bp.CellMetres       = template != null ? template.CellMetres : 4f;
            bp.WallHeightMetres = template != null ? template.WallHeightMetres : 4f;
            for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                bp.Blocks.Add(new BlockEntry
                {
                    Cell = new Vector3Int(x, 0, z),
                    Kind = BlockKind.Floor,
                    Tags = new[] { "default" },
                    Label = "Default",
                });
            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{bp.DisplayName}.asset");
            AssetDatabase.CreateAsset(bp, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BlockIO] Created '{bp.DisplayName}' at {path}.");
            return bp;
            #else
            return null;
            #endif
        }

        // Make `bp` the live blueprint: update the session data the bootstrapper
        // materialises from, persist it onto the BuildingDefinition so the next
        // door-entry uses it too, then regen the interior.
        public static void SetActive(BlockBlueprintAsset bp, InteriorSceneBootstrapper bs)
        {
            if (bp == null) return;
            InteriorSessionData.BlockBlueprint = bp;
            #if UNITY_EDITOR
            var def = bs != null ? bs.CurrentDefinition : null;
            if (def != null)
            {
                var so = new SerializedObject(def);
                var prop = so.FindProperty("blockBlueprint");
                if (prop != null)
                {
                    prop.objectReferenceValue = bp;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(def);
                    AssetDatabase.SaveAssets();
                }
            }
            #endif
            if (bs != null && bs.IsServer) bs.RegenerateFromBlockBlueprintFast();
        }

        private static void EnsureFolder()
        {
            #if UNITY_EDITOR
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Interiors", "Blueprints");
            #endif
        }
    }
}
