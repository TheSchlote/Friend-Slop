using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    public static class SerializedObjectExtensions
    {
        public static void SetFloat2FromVector(this SerializedProperty property, Vector2 value) 
        {        
            var p = property.Copy();
            p.Next(true);
            p.floatValue = value.x;
            p.Next(true);
            p.floatValue = value.y;        
        }
    }
}