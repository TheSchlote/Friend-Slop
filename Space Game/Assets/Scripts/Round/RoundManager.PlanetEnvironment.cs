using UnityEngine;

namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        private void OnCurrentPlanetCatalogIndexChanged(int previous, int current)
        {
            ApplyActivePlanetEnvironment();
        }

        private void HandlePlanetEnvironmentRegistered(PlanetEnvironment env)
        {
            // A planet scene that just additively loaded brings its env with it. Two
            // cases trigger a re-bind: the registered env is the active planet's env,
            // or it's a same-tier env that the active planet falls back to (catalog
            // entries without their own dedicated environment, like DeepHaul borrowing
            // Rusty Moon's scene).
            if (env == null || env.Planet == null) return;
            var current = CurrentPlanet;
            if (current == null) return;
            if (env.Planet == current || env.Planet.Tier == current.Tier)
                ApplyActivePlanetEnvironment();
        }

        private void ApplyActivePlanetEnvironment()
        {
            var planet = CurrentPlanet;

            // Server: reconcile per-planet scene loads. If the active planet has a scene
            // assigned, ensure it's loaded; if a previous planet scene is still around and
            // doesn't match, unload it. Scene loads are async - when the env Awakes inside
            // the new scene it fires Registered, which calls back into this method.
            if (IsServer)
                EnsurePlanetSceneOrchestrator()?.ServerReconcilePlanetScenes(planet, planetCatalog);

            // Search AllEnvironments so we can find and enable planets whose roots are disabled.
            var activeEnv = PlanetSceneOwnership.FindBindableEnvironment(planet);

            // Toggle planet roots. Even scene-loaded planets need this because authored
            // additive scenes are often left open in-editor; Netcode won't unload those,
            // so inactive planets must be hidden and made non-interactive here.
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env == null) continue;
                var want = env == activeEnv;

                env.SetActiveForRound(want);
                if (env.gameObject.activeSelf != want)
                    env.gameObject.SetActive(want);

                var content = env.ContentRoot;
                if (content != null && content != env.gameObject && content.activeSelf != want)
                    content.SetActive(want);
            }

            if (activeEnv != null)
            {
                activeEnv.SetActiveForRound(true);
                if (IsServer)
                    TrySpawnActivePlanetLootSpawners(activeEnv);
            }

            if (IsServer && activeEnv != null && PlanetSceneOwnership.HasAnyLiveSpawn(activeEnv.PlayerSpawnPoints))
                ConfigureSpawnPoints(activeEnv.PlayerSpawnPoints);
        }

        public bool IsEnvironmentActiveForCurrentPlanet(PlanetEnvironment env)
        {
            return env != null && env == PlanetSceneOwnership.FindBindableEnvironment(CurrentPlanet);
        }

        private bool TryPreparePlanetForRound()
        {
            var planet = CurrentPlanet;
            if (planet == null)
            {
                Debug.LogError("RoundManager: cannot start round because no current planet is selected.");
                return false;
            }

            ApplyActivePlanetEnvironment();
            var activeEnv = PlanetSceneOwnership.FindRoundReadyEnvironment(planet);
            if (activeEnv == null)
            {
                Debug.LogError(
                    $"RoundManager: cannot start round on '{planet.name}' because {PlanetSceneOwnership.GetReadinessProblem(planet)}.");
                return false;
            }

            activeEnv.SetActiveForRound(true);
            ConfigureSpawnPoints(activeEnv.PlayerSpawnPoints);
            return true;
        }
    }
}
