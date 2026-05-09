using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Microdetail
{
    [RequireComponent(typeof(Terrain))]
    [ExecuteAlways]
    public class AlphamapProceduralLayer : MonoBehaviour
    {
        private Terrain terrain;

        private Terrain Terrain
        {
            get
            {
                if (terrain == null)
                    terrain = GetComponent<Terrain>();

                return terrain;
            }
        }

        [SerializeField] [Range(0.0f, 4.0f)] private float strength = 0.5f; 
        [SerializeField, Range(0.0f, 1.0f)] private float threshold = 0.1f;
        [SerializeField] private int layerIndex = 0;
        [SerializeField] private Layer layer;
        [SerializeField] private Texture2D maskTexture;
        [SerializeField] private float maskTextureScale = 10.0f;
        
        private Brush brush;

        private void OnEnable()
        {
            brush = BrushRegistry.CreateBrush();
            OnValidate();
        }

        private void OnDisable()
        {
           brush?.Dispose();
           brush = null;
        }

        private void OnValidate()
        {
            if (brush == null)
                return;

            if (Terrain.terrainData == null)
            {
                brush.Dispose();
                brush = null;
                return;
            }

            var textureIndex = layerIndex / 4;
            var channelIndex = layerIndex % 4;
            var mask = (BrushMask)(1 << channelIndex);

            brush.Mode = BrushMode.Stamp;
            brush.Strength = strength;
            brush.Threshold = threshold;
            brush.Mask = mask;
            brush.Texture = Terrain.terrainData.GetAlphamapTexture(textureIndex);
            brush.Size = new Vector2(Terrain.terrainData.size.x, Terrain.terrainData.size.z);
            brush.Position = new Vector2(Terrain.transform.position.x, Terrain.transform.position.z) + brush.Size / 2.0f;
            brush.MaskTexture = maskTexture;
            brush.MaskTextureScale = maskTextureScale;
            brush.Layer = layer;
        }

        private void Update()
        {
           OnValidate();
        }
    }
}