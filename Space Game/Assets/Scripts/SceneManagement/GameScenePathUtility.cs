using System.IO;

namespace FriendSlop.SceneManagement
{
    public static class GameScenePathUtility
    {
        public static string NormalizePath(string scenePath)
        {
            return string.IsNullOrWhiteSpace(scenePath)
                ? string.Empty
                : scenePath.Trim().Replace('\\', '/');
        }

        public static bool IsUnityScenePath(string scenePath)
        {
            scenePath = NormalizePath(scenePath);
            return scenePath.StartsWith("Assets/")
                && scenePath.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase);
        }

        public static string GetSceneName(string scenePath)
        {
            scenePath = NormalizePath(scenePath);
            return string.IsNullOrEmpty(scenePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(scenePath);
        }
    }
}
