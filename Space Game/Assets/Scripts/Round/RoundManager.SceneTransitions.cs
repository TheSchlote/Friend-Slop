using FriendSlop.SceneManagement;

namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        private NetworkSceneTransitionService sceneTransitionService;

        public void ConfigureSceneTransitionService(NetworkSceneTransitionService service)
        {
            sceneTransitionService = service;
            EnsurePlanetSceneOrchestrator()?.Initialize(service);
        }
    }
}
