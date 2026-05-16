using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Microdetail
{
    public static class PreviewRenderingUtility
    {
        public static Texture2D GetPreviewTexture(MicrodetailAsset asset, bool forceGenerate, bool createIfMissing)
        {
            Texture2D previewTexture = null;
            
            var path = AssetDatabase.GetAssetPath(asset);
            previewTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!forceGenerate)
            {
                if (previewTexture != null)
                    return previewTexture;
            }

            if (!createIfMissing && previewTexture == null)
                return null;

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var loadedAsset in allAssets)
            {
                if (loadedAsset is Texture2D texture && texture.name == "Preview")
                    Object.DestroyImmediate(loadedAsset, true);
            }

            var preview = DrawPreview(asset, 256, 256);
            if (preview == null)
                return null;
            
            preview.name = "Preview";
            AssetDatabase.AddObjectToAsset(preview, asset);
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(preview);
            AssetDatabase.SaveAssetIfDirty(preview);
            AssetDatabase.SaveAssetIfDirty(asset);

            return preview;
        }
        
        public static Texture2D DrawPreview(MicrodetailAsset microdetailAsset, int width, int height)
        {
            var asset = microdetailAsset;
            if (asset == null)
                return null;
            
            if (asset.Material == null)
                return null;
            
            var block = new MaterialPropertyBlock();
            
            var materialInstance = Object.Instantiate(asset.Material);
            microdetailAsset.PrepareForRendering(materialInstance, block, null);
            
            materialInstance.DisableKeyword("MICRODETAIL_TERRAIN_BLENDING");
            materialInstance.EnableKeyword("MICRODETAIL_PREVIEW");
            
            var previewRenderUtility = new PreviewRenderUtility();
            previewRenderUtility.BeginStaticPreview(new Rect(0, 0, width, height));

            var mesh = new Mesh();
            MeshUtility.GenerateParallelepiped(mesh, microdetailAsset.AspectRatio);

            previewRenderUtility.DrawMesh(mesh, Matrix4x4.Rotate(Quaternion.Euler(microdetailAsset.InitialRotation)), materialInstance, 0, block);
            
            previewRenderUtility.camera.transform.position = new Vector3(-2, 2, -2);
            previewRenderUtility.camera.transform.LookAt(Vector3.zero);
            previewRenderUtility.camera.farClipPlane = 100;
            previewRenderUtility.camera.cullingMask = -1;
            var previewSceneField = previewRenderUtility.GetType().GetField("m_PreviewScene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var previewScene = previewSceneField.GetValue(previewRenderUtility);
            var sceneField = previewScene.GetType().GetField("m_Scene", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var scene = (Scene)sceneField.GetValue(previewScene);

            var sceneMask = EditorSceneManager.GetSceneCullingMask(scene);
            previewRenderUtility.camera.overrideSceneCullingMask = sceneMask;
            previewRenderUtility.camera.clearFlags = CameraClearFlags.Color;
            previewRenderUtility.camera.backgroundColor = Color.gray;
            foreach (var light in previewRenderUtility.lights)
                light.intensity = 3.0f;

            previewRenderUtility.Render(true);
            
            Object.DestroyImmediate(materialInstance);
            Object.DestroyImmediate(mesh);
            block.Clear();
            
            var result = previewRenderUtility.EndStaticPreview();
            previewRenderUtility.Cleanup();

            return result;
        }
    }
}