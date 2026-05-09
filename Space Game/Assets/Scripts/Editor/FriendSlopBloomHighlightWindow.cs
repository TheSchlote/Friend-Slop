#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    // Editor window for adding bloom-triggering emission to specific material slots on a
    // Renderer. Bloom is a screen-space effect driven by HDR brightness, so "add bloom to
    // part of a mesh" means "make that material emissive with an HDR color > 1.0". The
    // project already has a Bloom volume override configured (see Assets/glow PP.asset),
    // so anything emissive shows up bloomed automatically.
    //
    // How to use:
    //   1. Open via Tools/Friend Slop/Bloom Highlight.
    //   2. Drag a Renderer (or the GameObject containing one) into the field. Sub-meshes
    //      of the renderer's shared mesh appear as rows.
    //   3. Toggle "Glow", pick an HDR color (intensity > 1 for bloom), click Apply.
    //   4. The window creates Assets/Materials/Generated/<source>_Emissive.mat for each
    //      slot you toggled on, with _EMISSION enabled, and assigns it to the renderer's
    //      sharedMaterials. Re-running Apply after tweaks updates the same .mat in place,
    //      so prefab instances using these materials pick up the new color automatically.
    //
    // Caveats:
    //   - Single-submesh meshes only have one slot, so you can only make the entire mesh
    //     emissive. Splitting a single mesh into emissive parts requires editing the source
    //     model (Blender) so it exposes multiple material slots. There's no Unity-side
    //     mesh-painting workflow that doesn't either rebuild the mesh or use an emission
    //     map texture, both of which are out of scope here.
    //   - Operates on sharedMaterials, so changes persist into the scene/prefab. Don't run
    //     this with a prefab instance selected and then forget to "Apply Overrides" - the
    //     generated material asset is shared, but the slot ASSIGNMENT lives on the renderer.
    public sealed class FriendSlopBloomHighlightWindow : EditorWindow
    {
        private const string GeneratedMaterialFolder = "Assets/Materials/Generated";

        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private List<SlotConfig> slotConfigs = new();
        private Vector2 _scroll;

        [System.Serializable]
        private class SlotConfig
        {
            public bool enabled;
            [ColorUsage(true, true)] public Color emissionColor = new Color(2.5f, 2.0f, 1.0f, 1f);
        }

        [MenuItem("Tools/Friend Slop/Bloom Highlight")]
        public static void Open()
        {
            var window = GetWindow<FriendSlopBloomHighlightWindow>("Bloom Highlight");
            window.minSize = new Vector2(360f, 240f);
            window.SyncTargetFromSelection();
        }

        private void OnEnable() => SyncTargetFromSelection();
        private void OnSelectionChange()
        {
            // Auto-pick the renderer when the user clicks something in the hierarchy. Saves
            // a manual drag for the common "select object, open window, configure" flow.
            SyncTargetFromSelection();
            Repaint();
        }

        private void SyncTargetFromSelection()
        {
            if (Selection.activeGameObject == null) return;
            var renderer = Selection.activeGameObject.GetComponent<Renderer>();
            if (renderer != null && renderer != targetRenderer)
            {
                targetRenderer = renderer;
                ResizeSlotConfigs();
            }
        }

        private void ResizeSlotConfigs()
        {
            var slotCount = targetRenderer != null && targetRenderer.sharedMaterials != null
                ? targetRenderer.sharedMaterials.Length
                : 0;
            while (slotConfigs.Count < slotCount) slotConfigs.Add(new SlotConfig());
            while (slotConfigs.Count > slotCount) slotConfigs.RemoveAt(slotConfigs.Count - 1);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Bloom Highlight", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pick a Renderer, toggle which material slots should glow, set HDR colors, " +
                "and Apply. Generated materials are written to Assets/Materials/Generated/.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            var newRenderer = (Renderer)EditorGUILayout.ObjectField(
                "Target Renderer", targetRenderer, typeof(Renderer), allowSceneObjects: true);
            if (EditorGUI.EndChangeCheck())
            {
                targetRenderer = newRenderer;
                ResizeSlotConfigs();
            }

            if (targetRenderer == null)
            {
                EditorGUILayout.HelpBox("Select an object with a Renderer in the scene.", MessageType.Info);
                return;
            }

            ResizeSlotConfigs();

            var sharedMats = targetRenderer.sharedMaterials;
            if (sharedMats == null || sharedMats.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Renderer has no materials. Assign at least one before using this window.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Material Slots ({sharedMats.Length})", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < sharedMats.Length; i++)
            {
                var mat = sharedMats[i];
                var config = slotConfigs[i];

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Slot {i}", mat != null ? mat.name : "(empty)", EditorStyles.miniBoldLabel);
                    config.enabled = EditorGUILayout.Toggle("Glow", config.enabled);
                    using (new EditorGUI.DisabledScope(!config.enabled))
                    {
                        config.emissionColor = EditorGUILayout.ColorField(
                            new GUIContent("HDR Emission"), config.emissionColor,
                            showEyedropper: true, showAlpha: false, hdr: true);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply", GUILayout.Height(28))) Apply();
                if (GUILayout.Button("Restore Sources", GUILayout.Height(28))) RestoreSources();
            }
        }

        private void Apply()
        {
            if (targetRenderer == null) return;
            EnsureGeneratedFolderExists();

            var sharedMats = targetRenderer.sharedMaterials;
            // Copy because Renderer.sharedMaterials returns a clone of the array each access;
            // mutating in place doesn't write back. We assemble the new array and assign once.
            var nextMats = new Material[sharedMats.Length];
            var changed = false;

            for (var i = 0; i < sharedMats.Length; i++)
            {
                var current = sharedMats[i];
                var config = slotConfigs[i];
                if (!config.enabled || current == null)
                {
                    nextMats[i] = current;
                    continue;
                }

                var emissive = GetOrCreateEmissiveVariant(current);
                if (emissive != null)
                {
                    emissive.EnableKeyword("_EMISSION");
                    emissive.SetColor("_EmissionColor", config.emissionColor);
                    EditorUtility.SetDirty(emissive);
                    nextMats[i] = emissive;
                    changed = true;
                }
                else
                {
                    nextMats[i] = current;
                }
            }

            if (!changed)
            {
                EditorUtility.DisplayDialog("Bloom Highlight",
                    "No slots had Glow enabled - nothing applied.", "OK");
                return;
            }

            Undo.RecordObject(targetRenderer, "Apply Bloom Highlight");
            targetRenderer.sharedMaterials = nextMats;
            EditorUtility.SetDirty(targetRenderer);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // Swap each emissive slot back to the source material we duplicated from. Useful
        // when the user wants to undo a bloom pass without manually re-assigning materials.
        private void RestoreSources()
        {
            if (targetRenderer == null) return;
            var sharedMats = targetRenderer.sharedMaterials;
            var nextMats = new Material[sharedMats.Length];
            var changed = false;

            for (var i = 0; i < sharedMats.Length; i++)
            {
                var mat = sharedMats[i];
                nextMats[i] = mat;
                if (mat == null) continue;
                var source = TryFindSourceMaterial(mat);
                if (source != null && source != mat)
                {
                    nextMats[i] = source;
                    changed = true;
                }
            }

            if (!changed) return;
            Undo.RecordObject(targetRenderer, "Restore Bloom Highlight Sources");
            targetRenderer.sharedMaterials = nextMats;
            EditorUtility.SetDirty(targetRenderer);
            AssetDatabase.SaveAssets();
        }

        private static Material GetOrCreateEmissiveVariant(Material source)
        {
            if (source == null) return null;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            // Source material lives in memory only (e.g. a runtime instance); we can't anchor
            // the variant to its path, so produce a uniquely named asset based on the source name.
            var baseName = source.name.EndsWith("_Emissive")
                ? source.name
                : source.name + "_Emissive";
            var variantPath = $"{GeneratedMaterialFolder}/{baseName}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(variantPath);
            if (existing != null) return existing;

            var clone = new Material(source) { name = baseName };
            // Tag the clone with the source path so RestoreSources can swap back without us
            // tracking it externally. ProjectionPath is the only string slot that travels on
            // a Material - userData equivalents don't exist - so we encode it in a custom
            // shader keyword that's harmless at runtime.
            if (!string.IsNullOrEmpty(sourcePath))
                clone.SetOverrideTag("BloomHighlightSourcePath", sourcePath);

            AssetDatabase.CreateAsset(clone, variantPath);
            return clone;
        }

        private static Material TryFindSourceMaterial(Material variant)
        {
            if (variant == null) return null;
            var tag = variant.GetTag("BloomHighlightSourcePath", searchFallbacks: false, defaultValue: string.Empty);
            if (string.IsNullOrEmpty(tag)) return null;
            return AssetDatabase.LoadAssetAtPath<Material>(tag);
        }

        private static void EnsureGeneratedFolderExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(GeneratedMaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "Generated");

            // Defensive: if the folder vanished between the AssetDatabase check and now (rare,
            // but possible during reimports), fall back to a raw filesystem mkdir.
            if (!Directory.Exists(GeneratedMaterialFolder))
                Directory.CreateDirectory(GeneratedMaterialFolder);
        }
    }
}
#endif
