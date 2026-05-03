#if UNITY_EDITOR
using FriendSlop.Round;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private static RoundManager BuildRoundManagerPrefab()
        {
            // Idempotent. See note on BuildPlayerPrefab.
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath);
            if (existing != null)
            {
                return EnsureRoundManagerPrefabComponents();
            }

            var root = new GameObject("Round Manager");
            root.AddComponent<NetworkObject>();
            var round = root.AddComponent<RoundManager>();
            var orchestrator = root.AddComponent<PlanetSceneOrchestrator>();
            var serializedRound = new SerializedObject(round);
            serializedRound.FindProperty("quota").intValue = 0;
            serializedRound.FindProperty("roundLengthSeconds").floatValue = 0f;
            serializedRound.FindProperty("planetSceneOrchestrator").objectReferenceValue = orchestrator;
            serializedRound.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RoundManagerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<RoundManager>();
        }

        private static RoundManager EnsureRoundManagerPrefabComponents()
        {
            var root = PrefabUtility.LoadPrefabContents(RoundManagerPrefabPath);
            try
            {
                if (root.GetComponent<NetworkObject>() == null)
                    root.AddComponent<NetworkObject>();

                var round = root.GetComponent<RoundManager>();
                if (round == null)
                    round = root.AddComponent<RoundManager>();

                var orchestrator = root.GetComponent<PlanetSceneOrchestrator>();
                if (orchestrator == null)
                    orchestrator = root.AddComponent<PlanetSceneOrchestrator>();

                var serializedRound = new SerializedObject(round);
                serializedRound.FindProperty("planetSceneOrchestrator").objectReferenceValue = orchestrator;
                serializedRound.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RoundManagerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath)?.GetComponent<RoundManager>();
        }
    }
}
#endif
