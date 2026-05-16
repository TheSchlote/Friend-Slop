using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    [CustomEditor(typeof(Module), true)]
    public class ModuleEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawPropertiesExcluding(serializedObject, "m_Script");
        }
    }
}