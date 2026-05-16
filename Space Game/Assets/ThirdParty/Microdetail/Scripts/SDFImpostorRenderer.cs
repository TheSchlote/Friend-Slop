using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    public static class SDFImpostorRenderer
    {
        private static Material impostorRenderMaterial;
        private static readonly int UVSpaceSize = Shader.PropertyToID("_UVSpaceSize");

        private static Mesh quadMesh;
        private static readonly int Count = Shader.PropertyToID("_Count");
        private static readonly int Size = Shader.PropertyToID("_Size");

        private static Mesh QuadMesh 
        {
            get
            {
                if (quadMesh != null)
                    return quadMesh;

                quadMesh = CreateQuadMesh();
                return quadMesh;
            }
        }
        
        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh();

            mesh.vertices = new Vector3[]
            {
                new Vector3(-1, -1, 0),
                new Vector3( 1, -1, 0),
                new Vector3(-1,  1, 0),
                new Vector3( 1,  1, 0)
            };

            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };

            mesh.triangles = new int[]
            {
                0, 2, 1,
                2, 3, 1
            };

            mesh.RecalculateNormals();
            return mesh;
        }
        
        public static OctahedralImpostor Render(SDFAsset asset, int count, int resolution)
        {
            if (impostorRenderMaterial == null)
                impostorRenderMaterial = new Material(Resources.Load<Shader>("Microdetail/Shaders/SDFImpostorRenderer"));
            
            var singleEntryResolution = resolution / count;
            var targetAlbedo = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            targetAlbedo.useMipMap = true;
            var targetNormal = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            targetNormal.useMipMap = true;
            var targetProperties = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            targetNormal.useMipMap = true;
            
            var result = new OctahedralImpostor
                {
                    AlbedoMap = new Texture2D(resolution, resolution, TextureFormat.ARGB32, true, true),
                    NormalDepth = new Texture2D(resolution, resolution, TextureFormat.ARGB32, true, true),
                    PropertiesMap = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, true),
                    GridSize = count
                };
            
            var size = (Vector3)asset.AspectRatio;
            impostorRenderMaterial.SetVector(Size, size);
            impostorRenderMaterial.SetFloat(UVSpaceSize, singleEntryResolution / (float)resolution);
            impostorRenderMaterial.SetTexture(MicrodetailMaterialProperties.SDF, asset.SDF);
            impostorRenderMaterial.SetTexture(MicrodetailMaterialProperties.Albedo, asset.Albedo);
            impostorRenderMaterial.SetTexture(MicrodetailMaterialProperties.Mask, asset.Mask);
            impostorRenderMaterial.SetInt(MicrodetailMaterialProperties.MaxIterationsCount, asset.MaxSamplingIterationsCount);
            impostorRenderMaterial.SetVector(MicrodetailMaterialProperties.TextureSize, new Vector4(asset.SDF.width, asset.SDF.height, asset.SDF.depth));
            impostorRenderMaterial.SetFloat(Count, count);
            
            Graphics.Blit(Texture2D.whiteTexture, targetNormal);

            Graphics.SetRenderTarget(new RenderBuffer[]
                {
                    targetAlbedo.colorBuffer,
                    targetNormal.colorBuffer,
                    targetProperties.colorBuffer,
                }, targetNormal.depthBuffer);
            
            impostorRenderMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            Graphics.DrawMeshNow(QuadMesh, Matrix4x4.identity, 0);
            GL.PopMatrix();

            RenderTexture.active = targetAlbedo;
            result.AlbedoMap.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            
            RenderTexture.active = targetNormal;
            result.NormalDepth.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            
            RenderTexture.active = targetProperties;
            result.PropertiesMap.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            
            result.AlbedoMap.Compress(true);
            result.NormalDepth.Compress(true);
            result.PropertiesMap.Compress(true);
            
            Object.DestroyImmediate(targetAlbedo);
            Object.DestroyImmediate(targetNormal);
            Object.DestroyImmediate(targetProperties);

            return result;
        }
    }
}