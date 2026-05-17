using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    // Synty LowPolyInterior ships with materials targeting the Built-in pipeline's
    // Standard shader, which renders magenta under URP. This menu walks every
    // material in Assets/LowPolyInterior/Materials/ and swaps its shader to
    // Universal Render Pipeline/Lit, copying over the main texture and base
    // colour. Idempotent — re-running just confirms the shader is URP/Lit.
    //
    // Tools → Friend Slop → Interiors → Upgrade LowPolyInterior Materials to URP.
    internal static class LowPolyURPMaterialUpgrade
    {
        private const string MaterialsFolder = "Assets/LowPolyInterior/Materials";

        [MenuItem("Tools/Friend Slop/Interiors/Upgrade LowPolyInterior Materials to URP")]
        public static void Upgrade()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
            {
                EditorUtility.DisplayDialog("Friend Slop",
                    "Couldn't find shader 'Universal Render Pipeline/Lit'. " +
                    "Is the project still on URP?", "OK");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialsFolder });
            int swapped = 0;
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var mat  = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                if (mat.shader == urpLit) continue;
                // Snapshot texture + colour before changing shader (some props
                // map across shader name conventions).
                Texture mainTex   = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex")
                                  : mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                                  : null;
                Color   mainColor = mat.HasProperty("_Color")    ? mat.GetColor("_Color")
                                  : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                  : Color.white;
                mat.shader = urpLit;
                if (mainTex != null && mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", mainTex);
                if (mainTex != null && mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", mainTex);
                if (mat.HasProperty("_BaseColor"))                    mat.SetColor("_BaseColor", mainColor);
                if (mat.HasProperty("_Color"))                        mat.SetColor("_Color", mainColor);
                EditorUtility.SetDirty(mat);
                swapped++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[LowPolyURPUpgrade] Swapped shader on {swapped} materials in {MaterialsFolder}.");
            EditorUtility.DisplayDialog("Friend Slop",
                $"Upgraded {swapped} materials to URP/Lit.\nRe-enter the building to see them render.",
                "OK");
        }
    }
}
