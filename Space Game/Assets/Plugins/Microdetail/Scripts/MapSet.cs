using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Microdetail
{
    [Serializable]
    public abstract class MapSet : IDisposable
    {
        private RenderTexture proceduralMap;
        private RenderTexture map;
        private RenderTexture preview;
        [SerializeField] private Texture2D persistent;
        private bool isPersistentApplied = false;
        private bool renderPreview = false;
        [NonSerialized] private LayerRenderer parentRenderer;

        public bool IsValid => ParentRenderer != null;

        public bool PendingPreview => renderPreview;
        
        protected abstract int PixelSize { get; }
        public abstract GraphicsFormat Format { get; }
        protected abstract string PersistentName { get; }

        protected LayerRenderer ParentRenderer
        {
            get => parentRenderer;
            set => parentRenderer = value;
        }
        
        protected abstract bool DefaultToMax { get; }
        
        public bool HasPersistent => persistent != null;

        public Texture2D Persistent
        {
            get
            {
                if (persistent != null)
                {
                    if (!isPersistentApplied)
                    {
                        persistent.Apply();
                        isPersistentApplied = true;
                    }

                    return persistent;
                }

                persistent = new Texture2D(ParentRenderer.MapSize, ParentRenderer.MapSize, Format, 0, TextureCreationFlags.None);
                persistent.name = PersistentName;
                
                var data = new NativeArray<byte>(ParentRenderer.MapSize * ParentRenderer.MapSize * PixelSize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var value = DefaultToMax ? (byte)255 : (byte)0;
                for (var index = 0; index < data.Length; index++)
                    data[index] = value;
                
                persistent.SetPixelData(data, 0);
                persistent.Apply(false, false);

                data.Dispose();
                
                return persistent;
            }
        }

        public RenderTexture TargetTexture => proceduralMap == null ? Map : ProceduralMap;
        
        public RenderTexture Map
        {
            get
            {
                if (map != null) 
                    return map;
                
                map = new RenderTexture(ParentRenderer.MapSize, ParentRenderer.MapSize, 0, Format);
                map.wrapMode = TextureWrapMode.Clamp;
                Graphics.Blit(Persistent, map);

                return map;
            }
        }
        
        public RenderTexture ProceduralMap
        {
            get
            {
                if (proceduralMap != null) 
                    return proceduralMap;

                proceduralMap = new RenderTexture(ParentRenderer.MapSize, ParentRenderer.MapSize, 0, Format);
                proceduralMap.wrapMode = TextureWrapMode.Clamp;

                return proceduralMap;
            }
        }
        
        public bool HasProcedural => proceduralMap != null;

        public RenderTexture EnsureHasMap()
        {
            return Map;
        }

        public void ClearProceduralMap()
        {
            Object.DestroyImmediate(proceduralMap);
            proceduralMap = null;
        }

        public RenderTexture TextureToDraw => PendingPreview ? Preview : TargetTexture;

        public RenderTexture Preview
        {
            get
            {
                if (preview != null) 
                    return preview;

                preview = new RenderTexture(ParentRenderer.MapSize, ParentRenderer.MapSize, 0, Format);
                preview.wrapMode = TextureWrapMode.Clamp;
                Graphics.Blit(TargetTexture, preview);

                return preview;
            }
        }
        
        public void SetParent(LayerRenderer renderer) => ParentRenderer = renderer;

        public void MarkToRenderPreview()
        {
            renderPreview = true;
        }

        public void ClearPreview()
        {
            renderPreview = false;
        }

        public void UpdateFromPersistent()
        {
            Graphics.Blit(persistent, Map);
            Graphics.Blit(persistent, Preview);
        }
        
#if UNITY_EDITOR
        public void Resize(TerrainData data, int newSize)
        {
            if (Persistent.width == newSize)
                return;
            
            var tempMap = Map;
            map = new RenderTexture(newSize, newSize, 0, Format);
            map.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(tempMap, map);
            
            if (tempMap != null)
                Object.DestroyImmediate(tempMap);
            
            if (preview != null)
                Object.DestroyImmediate(preview);
            
            UpdatePersistentWithCurrent(data);
        }
        
        public void UpdatePersistentWithCurrent(TerrainData data)
        {
            var previousActive = RenderTexture.active;
            RenderTexture.active = Map;
            if (Persistent.width != Map.width)
            {
                Persistent.Reinitialize(Map.width, Map.height);
                Persistent.Apply();
            }

            Persistent.ReadPixels(new Rect(0, 0, Map.width, Map.height), 0, 0);
            RenderTexture.active = previousActive;
            Persistent.Apply();
            
            UnityEditor.EditorUtility.SetDirty(Persistent);
            if (UnityEditor.AssetDatabase.Contains(Persistent)) 
                return;
            
            var terrainData = UnityEditor.EditorUtility.InstanceIDToObject(data.GetInstanceID());
            UnityEditor.AssetDatabase.AddObjectToAsset(Persistent, terrainData);
        }

        public void SaveIfDirty()
        {
            UnityEditor.AssetDatabase.SaveAssetIfDirty(Persistent);
        }
#endif
        public void Dispose()
        {
            if (RenderTexture.active == map)
                RenderTexture.active = null;
            
            if (RenderTexture.active == preview)
                RenderTexture.active = null;
            
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(preview);
            
#if UNITY_EDITOR
            UnityEditor.Undo.DestroyObjectImmediate(persistent);
#else
            Object.DestroyImmediate(persistent, true);
#endif
        }
    }

    [Serializable]
    public class DensityMapSet : MapSet
    {
        protected override int PixelSize => 1;
        public override GraphicsFormat Format => GraphicsFormat.R8_UNorm;
        protected override string PersistentName => $"Density map {ParentRenderer.Guid}";
        protected override bool DefaultToMax => false;
    }
    
    [Serializable]
    public class TintMapSet : MapSet
    {
        protected override int PixelSize => 4;
        public override GraphicsFormat Format => GraphicsFormat.R8G8B8A8_UNorm;
        protected override string PersistentName => $"Tint map {ParentRenderer.Guid}";
        protected override bool DefaultToMax => true;
    }
}