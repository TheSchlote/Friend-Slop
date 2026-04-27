using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.SceneManagement
{
    [CreateAssetMenu(menuName = "Friend Slop/Scene Catalog", fileName = "SceneCatalog")]
    public class GameSceneCatalog : ScriptableObject
    {
        [SerializeField] private List<GameSceneDefinition> scenes = new();

        public IReadOnlyList<GameSceneDefinition> AllScenes => scenes;

        public GameSceneDefinition GetFirstByRole(GameSceneRole role)
        {
            if (scenes == null) return null;
            for (var i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                if (scene != null && scene.Role == role)
                    return scene;
            }

            return null;
        }

        public GameSceneDefinition GetByPath(string scenePath)
        {
            scenePath = GameScenePathUtility.NormalizePath(scenePath);
            if (string.IsNullOrEmpty(scenePath) || scenes == null) return null;
            for (var i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];
                if (scene != null && scene.ScenePath == scenePath)
                    return scene;
            }

            return null;
        }
    }
}
