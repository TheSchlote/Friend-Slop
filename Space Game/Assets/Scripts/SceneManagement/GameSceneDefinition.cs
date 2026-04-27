using UnityEngine;

namespace FriendSlop.SceneManagement
{
    [CreateAssetMenu(menuName = "Friend Slop/Scene Definition", fileName = "SceneDefinition")]
    public class GameSceneDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Unnamed Scene";
        [SerializeField] private string scenePath;
        [SerializeField] private GameSceneRole role = GameSceneRole.Planet;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ScenePath => GameScenePathUtility.NormalizePath(scenePath);
        public string SceneName => GameScenePathUtility.GetSceneName(scenePath);
        public GameSceneRole Role => role;
        public bool IsConfigured => GameScenePathUtility.IsUnityScenePath(scenePath);
        public bool ShouldLoadAdditively => role != GameSceneRole.Bootstrap;
    }
}
