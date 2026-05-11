using UnityEngine;
using UnityEditor;

namespace Microdetail
{
    [CustomPropertyDrawer(typeof(LayerReference))]
    public class LayerRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var layerProp = property.FindPropertyRelative("layer");

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();

            var newLayer = EditorGUI.LayerField(position, label, Mathf.Clamp(layerProp.intValue, 0, 31));
            if (EditorGUI.EndChangeCheck())
                layerProp.intValue = Mathf.Clamp(newLayer, 0, 31);

            EditorGUI.EndProperty();
        }
    }
}