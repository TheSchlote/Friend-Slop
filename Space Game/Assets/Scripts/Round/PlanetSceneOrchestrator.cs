using System.Collections;
using System.Collections.Generic;
using FriendSlop.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Round
{
    public sealed class PlanetSceneOrchestrator : NetworkBehaviour
    {
        public const float SceneEventRetrySeconds = 12f;

        private NetworkSceneTransitionService sceneTransitionService;
        private string activePlanetScenePath = string.Empty;
        private Coroutine planetSceneReconcileCoroutine;
        private string planetSceneReconcileTargetPath = string.Empty;
        private readonly HashSet<string> warnedMissingPlanetScenes = new();
        private readonly HashSet<string> warnedFailedPlanetSceneLoads = new();

        private bool HasServerAuthority =>
            IsServer || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);

        private NetworkSceneTransitionService SceneTransitionService =>
            sceneTransitionService != null ? sceneTransitionService : NetworkSceneTransitionService.Instance;

        public void Initialize(NetworkSceneTransitionService service)
        {
            sceneTransitionService = service;
        }

        public void ServerReconcilePlanetScenes(PlanetDefinition planet, PlanetCatalog catalog)
        {
            if (!HasServerAuthority) return;

            var sceneOwner = ResolveSceneOwner(planet, catalog);
            var targetPath = sceneOwner != null ? sceneOwner.PlanetScene.ScenePath : string.Empty;
            if (planetSceneReconcileCoroutine != null)
            {
                if (string.Equals(planetSceneReconcileTargetPath, targetPath,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                StopCoroutine(planetSceneReconcileCoroutine);
                planetSceneReconcileCoroutine = null;
            }

            // Defer to next frame so this never runs while NetworkManager's
            // HostServerInitialize is still on the stack. The legacy fallback in
            // RoundManager keeps the active env bound during the one-frame gap.
            planetSceneReconcileTargetPath = targetPath;
            planetSceneReconcileCoroutine = StartCoroutine(ServerReconcilePlanetScenesDeferred(sceneOwner, targetPath));
        }

        private IEnumerator ServerReconcilePlanetScenesDeferred(PlanetDefinition sceneOwner, string targetPath)
        {
            yield return null;
            if (this == null || !HasServerAuthority)
            {
                FinishPlanetSceneReconcile(targetPath);
                yield break;
            }

            var service = SceneTransitionService;
            var hasScene = sceneOwner != null;

            if (!string.IsNullOrEmpty(activePlanetScenePath)
                && !string.Equals(activePlanetScenePath, targetPath, System.StringComparison.OrdinalIgnoreCase))
            {
                if (service == null)
                {
                    WarnFailedPlanetSceneLoad(activePlanetScenePath,
                        "no NetworkSceneTransitionService exists in the active scene");
                    activePlanetScenePath = string.Empty;
                }
                else
                {
                    yield return ServerUnloadActivePlanetScene(service, targetPath);
                }
            }

            if (!hasScene)
            {
                FinishPlanetSceneReconcile(targetPath);
                yield break;
            }

            if (WarnMissingPlanetSceneIfNeeded(targetPath))
            {
                FinishPlanetSceneReconcile(targetPath);
                yield break;
            }

            if (service == null)
            {
                WarnFailedPlanetSceneLoad(targetPath,
                    "no NetworkSceneTransitionService exists in the active scene");
                FinishPlanetSceneReconcile(targetPath);
                yield break;
            }

            if (service.WasServerSceneLoadStarted(targetPath))
            {
                activePlanetScenePath = targetPath;
                FinishPlanetSceneReconcile(targetPath);
                yield break;
            }

            var deadline = Time.time + SceneEventRetrySeconds;
            while (true)
            {
                var status = service.ServerLoadScene(sceneOwner.PlanetScene);
                if (status == SceneEventProgressStatus.Started)
                {
                    activePlanetScenePath = targetPath;
                    FinishPlanetSceneReconcile(targetPath);
                    yield break;
                }

                if (status == SceneEventProgressStatus.SceneEventInProgress && Time.time < deadline)
                {
                    yield return null;
                    service = SceneTransitionService;
                    if (service == null)
                    {
                        WarnFailedPlanetSceneLoad(targetPath,
                            "NetworkSceneTransitionService disappeared while waiting for Netcode scene event");
                        FinishPlanetSceneReconcile(targetPath);
                        yield break;
                    }
                    continue;
                }

                WarnFailedPlanetSceneLoad(targetPath, $"Netcode returned {status}");
                FinishPlanetSceneReconcile(targetPath);
                yield break;
            }
        }

        private IEnumerator ServerUnloadActivePlanetScene(NetworkSceneTransitionService service, string nextTargetPath)
        {
            var previousPath = activePlanetScenePath;
            var deadline = Time.time + SceneEventRetrySeconds;
            while (!string.IsNullOrEmpty(previousPath))
            {
                var status = service.ServerUnloadScenePath(previousPath);
                if (status == SceneEventProgressStatus.Started || status == SceneEventProgressStatus.SceneNotLoaded)
                {
                    activePlanetScenePath = string.Empty;
                    yield break;
                }

                if (status == SceneEventProgressStatus.SceneEventInProgress && Time.time < deadline)
                {
                    yield return null;
                    service = SceneTransitionService;
                    if (service != null) continue;
                    WarnFailedPlanetSceneLoad(nextTargetPath,
                        "NetworkSceneTransitionService disappeared while unloading previous planet scene");
                    activePlanetScenePath = string.Empty;
                    yield break;
                }

                Debug.LogWarning(
                    $"RoundManager: could not unload previous planet scene '{previousPath}' before loading '{nextTargetPath}' " +
                    $"(Netcode returned {status}).");
                activePlanetScenePath = string.Empty;
                yield break;
            }
        }

        private void FinishPlanetSceneReconcile(string targetPath)
        {
            if (!string.Equals(planetSceneReconcileTargetPath, targetPath,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            planetSceneReconcileCoroutine = null;
            planetSceneReconcileTargetPath = string.Empty;
        }

        private void WarnFailedPlanetSceneLoad(string scenePath, string reason)
        {
            if (warnedFailedPlanetSceneLoads.Add($"{scenePath}|{reason}"))
                Debug.LogError($"RoundManager: cannot load planet scene '{scenePath}' ({reason}).");
        }

        private bool WarnMissingPlanetSceneIfNeeded(string scenePath)
        {
            if (SceneUtility.GetBuildIndexByScenePath(scenePath) >= 0)
                return false;

            if (warnedMissingPlanetScenes.Add(scenePath))
            {
                Debug.LogWarning(
                    $"RoundManager: planet scene '{scenePath}' is not in Build Settings; skipping additive load. " +
                    "(Run Tools/Friend Slop/Extract Tier 1 Into Scene to author it.)");
            }

            return true;
        }

        private static PlanetDefinition ResolveSceneOwner(PlanetDefinition planet, PlanetCatalog catalog)
        {
            if (planet == null) return null;
            if (planet.HasPlanetScene) return planet;
            if (catalog == null) return null;

            for (var i = 0; i < catalog.Count; i++)
            {
                var candidate = catalog.GetByIndex(i);
                if (candidate == null) continue;
                if (candidate == planet) continue;
                if (candidate.Tier != planet.Tier) continue;
                if (candidate.HasPlanetScene) return candidate;
            }
            return null;
        }
    }
}
