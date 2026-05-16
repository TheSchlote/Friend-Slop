using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    public static class SaveModeSaveHandler
    {
        [System.Serializable]
        private struct SerializedRenderer
        {
            public int InstanceID;
            public string State;

            public SerializedRenderer(GameObject parent, string state)
            {
                InstanceID = parent.GetInstanceID();
                State = state;
            }
        }
        
        private static List<string> serializedObjects = new List<string>();

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            serializedObjects ??= new List<string>();
            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    var renderers = Object.FindObjectsByType<MicrodetailRenderer>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.InstanceID);
                    
                    foreach (var renderer in renderers)
                    {
                        var parent = renderer.transform.parent;
                        if (parent == null)
                            continue;
                        
                        var parentGameObject = parent.gameObject;
                        
                        var entry = new SerializedRenderer(parentGameObject, EditorJsonUtility.ToJson(renderer));
                        var serializedObject = EditorJsonUtility.ToJson(entry);
                        
                        serializedObjects.Add(serializedObject);
                    }
                    break;
                case PlayModeStateChange.EnteredEditMode:
                {
                    var terrains = Resources.FindObjectsOfTypeAll<Terrain>().ToList();
                    terrains.RemoveAll(x => !x.gameObject.scene.IsValid());
                    
                    foreach (var serialized in serializedObjects)
                    {
                        object entry = new SerializedRenderer();
                        EditorJsonUtility.FromJsonOverwrite(serialized, entry);
                        var deserializedObject = (SerializedRenderer)entry;
                        var terrain = terrains.Find(x => x.gameObject.GetInstanceID() == deserializedObject.InstanceID);
                        if (terrain == null)
                        {
                            Debug.LogWarning($"Couldn't find terrain for {deserializedObject.InstanceID}. Was it created during runtime?");
                            continue;
                        }

                        var renderer = RendererUtility.GetMicrodetailRenderer(terrain);
                        var currentState = EditorJsonUtility.ToJson(renderer);
                        if (currentState == deserializedObject.State)
                            continue;
                        
                        EditorJsonUtility.FromJsonOverwrite(deserializedObject.State, renderer);
                        EditorUtility.SetDirty(renderer);
                    }

                    serializedObjects.Clear();
                    break;
                }
            }
        }
    }
}