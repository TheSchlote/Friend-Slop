using UnityEditor;

namespace Microdetail
{
    [CustomEditor(typeof(CameraSettings))]
    public class CameraSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Microdetail camera settings provide you an ability to customize the behaviour of the camera in relation to the microdetail system.", MessageType.Info);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludeFromRendering"));
            serializedObject.ApplyModifiedProperties();
        }
    }
}