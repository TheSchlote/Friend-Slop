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
                return existing.GetComponent<RoundManager>();
            }

            var root = new GameObject("Round Manager");
            root.AddComponent<NetworkObject>();
            var round = root.AddComponent<RoundManager>();
            var serializedRound = new SerializedObject(round);
            serializedRound.FindProperty("quota").intValue = 0;
            serializedRound.FindProperty("roundLengthSeconds").floatValue = 0f;
            serializedRound.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RoundManagerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<RoundManager>();
        }
    }
}
#endif
