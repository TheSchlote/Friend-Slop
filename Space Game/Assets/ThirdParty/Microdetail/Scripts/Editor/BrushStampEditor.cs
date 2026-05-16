using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    [CustomEditor(typeof(BrushStamp))]
    public class BrushStampEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawPropertiesExcluding(serializedObject, "m_Script");

            serializedObject.ApplyModifiedProperties();
        }
    }
}