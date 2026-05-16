using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Hazards
{
    public class AnomalySpawner : MonoBehaviour
    {
        [SerializeField] private GameObject[] anomalyPrefabs = new GameObject[0];
        [SerializeField] private int maxOrbs = 3;
        [SerializeField] private float minSpawnInterval = 30f;
        [SerializeField] private float maxSpawnInterval = 60f;

        private float _nextSpawnTime;
        private bool _wasRoundActive;
        private readonly List<AnomalyOrb> _activeOrbs = new();

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            var round = RoundManagerRegistry.Current;
            if (round == null) return;

            var roundActive = round.Phase.Value == RoundPhase.Active;

            if (!_wasRoundActive && roundActive)
                _nextSpawnTime = Time.time + Random.Range(minSpawnInterval * 0.4f, minSpawnInterval);

            _wasRoundActive = roundActive;

            if (!roundActive) return;

            if (Time.time >= _nextSpawnTime)
            {
                _nextSpawnTime = Time.time + Random.Range(minSpawnInterval, maxSpawnInterval);
                TrySpawnOrb();
            }
        }

        private void TrySpawnOrb()
        {
            if (anomalyPrefabs.Length == 0) return;

            // Per-planet opt-out: PlanetDefinition.SuppressAnomalies is the data-driven
            // gate for "no anomalies on this planet" - test sandboxes and any future
            // cozy/cinematic planets flip it on and the global spawner skips them. Per-
            // scene AnomalySpawner instances (Ice Planet's IceMine spawner, etc.) are
            // separate components and ignore this check.
            var round = RoundManagerRegistry.Current;
            var activePlanet = round != null ? round.CurrentPlanet : null;
            if (activePlanet != null && activePlanet.SuppressAnomalies) return;

            var liveCount = 0;
            foreach (var active in _activeOrbs)
            {
                if (active != null && active.IsSpawned) liveCount++;
            }
            if (liveCount >= maxOrbs) return;

            var world = GetCurrentPlanet();
            if (world == null) return;

            var spawnPos = world.GetSurfacePoint(Random.onUnitSphere, 2f);
            var prefab = anomalyPrefabs[Random.Range(0, anomalyPrefabs.Length)];
            if (prefab == null) return;

            // Spawn into the active planet scene with destroyWithScene:true so anomalies
            // tear down on planet travel instead of leaking into DontDestroyOnLoad (or a
            // never-unloaded bootstrap scene). Same active-scene contract as loot and
            // meteors; see PlanetLootSpawner.TrySpawnNow.
            var targetScene = ResolveActivePlanetScene();
            var previousActiveScene = SceneManager.GetActiveScene();
            var shouldRestoreActiveScene = targetScene.IsValid() && targetScene.isLoaded && targetScene != previousActiveScene;
            if (shouldRestoreActiveScene)
                SceneManager.SetActiveScene(targetScene);

            try
            {
                var go = Instantiate(prefab, spawnPos, Quaternion.identity);
                go.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
                var orb = go.GetComponent<AnomalyOrb>();
                orb.ServerInitialize(spawnPos);
                _activeOrbs.Add(orb);
            }
            finally
            {
                if (shouldRestoreActiveScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static SphereWorld GetCurrentPlanet()
        {
            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player != null && player.IsSpawned)
                    return SphereWorld.GetClosest(player.transform.position);
            }
            return SphereWorld.GetClosest(Vector3.zero);
        }

        private Scene ResolveActivePlanetScene()
        {
            var round = RoundManagerRegistry.Current;
            if (round != null)
            {
                var envs = PlanetEnvironment.ActiveEnvironments;
                for (var i = 0; i < envs.Count; i++)
                {
                    var env = envs[i];
                    if (env != null && round.IsEnvironmentActiveForCurrentPlanet(env))
                    {
                        var scene = env.gameObject.scene;
                        if (scene.IsValid() && scene.isLoaded)
                            return scene;
                    }
                }
            }
            // Scene-local spawners (e.g. Ice Planet's IceMine) already live in their
            // planet scene; the global bootstrap spawner falls back to its own scene.
            return gameObject.scene;
        }
    }
}
