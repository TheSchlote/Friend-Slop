using System;
using UnityEngine;

namespace Microdetail
{
    public enum BrushMode
    {
        Stamp,
        Eraser
    }
    
    [ExecuteAlways]
    public class BrushStamp : MonoBehaviour
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("GameObject/Microdetail/Brush stamp", false, 10)]
        private static void CreateMyObject()
        {
            var obj = new GameObject("Brush stamp");
            obj.AddComponent<BrushStamp>();

            if (UnityEditor.SceneView.lastActiveSceneView != null)
                obj.transform.position = UnityEditor.SceneView.lastActiveSceneView.pivot;

            UnityEditor.Undo.RegisterCreatedObjectUndo(obj, "Create brush stamp");
            UnityEditor.Selection.activeGameObject = obj;
        }
#endif
        
        [SerializeField] private Layer layer;
        [SerializeField] private Texture texture;
        [SerializeField] private BrushMode brushMode;
        [Range(0.0f, 1.0f)] [SerializeField] private float strength = 0.25f;

        public Texture Texture
        {
            set
            {
                if (texture == value)
                    return;
                
                texture = value;
                MakeDirty();
            }

            get => texture;
        }

        public Layer Layer
        {
            set
            {
                if (layer == value)
                    return;
                
                layer = value;
                MakeDirty();
            }
            
            get => layer;
        }

        public BrushMode BrushMode
        {
            set
            {
                if (brushMode == value)
                    return;
                
                brushMode = value;
                MakeDirty();
            }
            get => brushMode;
        }
        
        private Brush brush;
        
        private void OnEnable()
        {
            brush = BrushRegistry.CreateBrush();
            brush.Layer = layer;
        }

        private void OnValidate()
        {
            if (brush == null)
                return;
            
            Update();
        }

        private void OnDisable()
        {
            brush.Dispose();
            brush = null;
        }

        private void Update()
        {
            if (brush == null)
                return;

            var cachedTransform = transform;
            var position = cachedTransform.position;

            brush.Layer = layer;
            brush.Position = new Vector2(position.x, position.z);
            brush.Size = new Vector2(cachedTransform.localScale.x, cachedTransform.localScale.z);
            brush.Angle = Mathf.Deg2Rad * cachedTransform.eulerAngles.y;
            brush.Texture = texture;
            brush.Mode = brushMode;
            brush.Strength = strength;
            brush.Mask = BrushMask.R;
            
            cachedTransform.eulerAngles = new Vector3(0.0f, cachedTransform.eulerAngles.y, 0.0f);
        }

        public void MakeDirty()
        {
            if (brush == null)
                return;
            
            brush.MakeDirty();
        }

        private void OnDrawGizmosSelected()
        {
            var transformation = Matrix4x4.TRS(transform.position, Quaternion.Euler(0.0f, transform.eulerAngles.y, 0.0f), transform.localScale);
            Gizmos.matrix = transformation;

            Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.2f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
        }
    }
}