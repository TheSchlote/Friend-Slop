using System;
using System.Collections.Generic;
using UnityEngine;

namespace Microdetail
{
    public static class BrushRegistry
    {
        private static List<Brush> brushes = new List<Brush>();
        private static HashSet<Brush> dirtyBrushes = new HashSet<Brush>();
        private static HashSet<Layer> dirtyLayers = new HashSet<Layer>();

        public static bool IsDirty(Layer layer)
        {
            return dirtyLayers.Contains(layer);
        }
        
        public static Brush CreateBrush()
        {
            var result = new Brush();
            
            brushes.Add(result);
            dirtyBrushes.Add(result);

            return result;
        }

        public static void GetBrushes(List<Brush> result)
        {
            result.Clear();
            result.AddRange(brushes);
        }
        
        internal static void MarkBrushDirty(Brush brush)
        {
            if (brush.Layer == null)
                return;
            
            dirtyBrushes.Add(brush);
            dirtyLayers.Add(brush.Layer);
        }

        internal static void RemoveBrush(Brush brush)
        {
            if (brush == null)
                return;

            if (brush.Layer != null)
                dirtyLayers.Add(brush.Layer);
            
            brushes.Remove(brush);
            dirtyBrushes.Remove(brush);
        }

        internal static void ClearDirty()
        {
            dirtyLayers.Clear();
            dirtyBrushes.Clear();
        }

        public static void MarkLayerDirty(Layer layer)
        {
            foreach (var brush in brushes)
            {
                if (brush.Layer == layer)
                    MarkBrushDirty(brush);
            }
            
            dirtyLayers.Add(layer);
        }
    }

    [Flags]
    public enum BrushMask
    {
       R = 1,
       G = 2,
       B = 4,
       A = 8
    }
    
    public class Brush : IDisposable
    {
        private BrushMask mask = BrushMask.R | BrushMask.G | BrushMask.B | BrushMask.A;

        public BrushMask Mask
        {
            get => mask;
            set
            {
                if (mask == value)
                    return;
                
                mask = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }
        
        private BrushMode mode;
        public BrushMode Mode
        {
            get => mode;
            set
            {
                if (mode == value)
                    return;

                mode = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }

        private float threshold = 0.0f;

        public float Threshold
        {
            get => threshold;
            set
            {
                if (Mathf.Approximately(threshold, value))
                    return;

                threshold = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }

        private float strength = 0.25f;
        public float Strength
        {
            get => strength;
            set
            {
                if (Mathf.Approximately(strength, value))
                    return;
                
                strength = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }
        
        private Texture texture;
        public Texture Texture
        {
            get => texture;
            set
            {
                if (texture == value)
                    return;

                texture = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }

        private Texture maskTexture;
        public Texture MaskTexture
        {
            get => maskTexture;
            set
            {
                if (maskTexture == value)
                    return;

                maskTexture = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }

        private float maskTextureScale = 10.0f;

        public float MaskTextureScale
        {
            get => maskTextureScale;
            set
            {
                if (Mathf.Approximately(maskTextureScale, value))
                    return;

                maskTextureScale = value;
                BrushRegistry.MarkBrushDirty(this); 
            }
        }
        
        private float angle = 0.0f;
        public float Angle
        {
            get => angle;
            set
            {
                if (Mathf.Approximately(angle, value))
                    return;

                angle = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }
        
        private Vector2 size = new Vector2(5.0f, 5.0f);
        public Vector2 Size
        {
            get => size;
            set
            {
                if (size == value)
                    return;

                size = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }
        
        Vector2 position = Vector2.zero;

        public Vector2 Position
        {
            get => position;
            set
            {
                if (position == value)
                    return;

                position = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }

        private Layer layer = null;

        public Layer Layer
        {
            get => layer;
            set
            {
                if (layer == value)
                    return;
                
                if (layer != null)
                    BrushRegistry.MarkLayerDirty(layer);
                    
                layer = value;
                BrushRegistry.MarkBrushDirty(this);
            }
        }

        public void MakeDirty()
        {
            BrushRegistry.MarkBrushDirty(this);
        }

        public void Dispose()
        {
            BrushRegistry.RemoveBrush(this);
        }
    }
}