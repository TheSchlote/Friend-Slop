using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    [CustomPropertyDrawer(typeof(Range))]
    [CustomPropertyDrawer(typeof(RangeSliderAttribute))]
    public class RangePropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var minProp = property.FindPropertyRelative("Min");
            var maxProp = property.FindPropertyRelative("Max");

            var minLimit = 0.0f;
            var maxLimit = 1.0f;

            if (attribute is RangeSliderAttribute rangeSlider)
            {
                minLimit = rangeSlider.MinLimit;
                maxLimit = rangeSlider.MaxLimit;
            }

            EditorGUI.BeginProperty(position, label, property);

            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            var sliderRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
                position.width - EditorGUIUtility.labelWidth, position.height);

            EditorGUI.LabelField(labelRect, label);

            var min = minProp.floatValue;
            var max = maxProp.floatValue;

            var fieldWidth = 45f;
            var minFieldRect = new Rect(sliderRect.x, sliderRect.y, fieldWidth, sliderRect.height);
            var maxFieldRect = new Rect(sliderRect.xMax - fieldWidth, sliderRect.y, fieldWidth, sliderRect.height);
            var sliderOnlyRect = new Rect(minFieldRect.xMax + 5f, sliderRect.y,
                sliderRect.width - 2 * fieldWidth - 10f, sliderRect.height);

            EditorGUI.MinMaxSlider(sliderOnlyRect, ref min, ref max, minLimit, maxLimit);
            min = EditorGUI.FloatField(minFieldRect, Mathf.Clamp(min, minLimit, max));
            max = EditorGUI.FloatField(maxFieldRect, Mathf.Clamp(max, min, maxLimit));

            minProp.floatValue = Mathf.Clamp(min, minLimit, maxLimit);
            maxProp.floatValue = Mathf.Clamp(max, minLimit, maxLimit);

            EditorGUI.EndProperty();
        }
    }
}