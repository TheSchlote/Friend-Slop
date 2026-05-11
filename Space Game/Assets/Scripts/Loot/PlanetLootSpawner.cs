using System.Collections;
using System.Collections.Generic;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Loot
{
    // Spawns loot from a LootPool when its planet becomes active in a running session.
    // Tier 2+ planets start inactive and only enable when the round travels there, so we
    // can't spawn at session-start like the global bootstrapper — we wait until OnEnable
    // and then roll once. If the planet is already active when the server starts (e.g.
    // tier 1), we also subscribe to OnServerStarted so we don't miss the trigger.
    [DisallowMultipleComponent]
    public class PlanetLootSpawner : MonoBehaviour
    {
        [SerializeField] private LootPool lootPool;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField, Min(1)] private int rollsPerSpawnPoint = 1;
        [SerializeField, Min(0f)] private float spawnSurfaceLift = 0.05f;

        private bool spawnedThisSession;
        private readonly List<NetworkObject> spawnedObjects = new();
        private PlanetEnvironment planetEnvironment;
        private NetworkManager subscribedManager;
        private bool waitingForLoadedScene;

        public LootPool LootPool => lootPool;
        public Transform[] SpawnPoints => spawnPoints;

        public void Configure(LootPool pool, Transform[] points, int rolls = 1)
        {
            lootPool = pool;
            spawnPoints = points;
            rollsPerSpawnPoint = Mathf.Max(1, rolls);
        }

        public void TrySpawnForActivePlanet()
        {
            TrySpawnNow();
        }

        public void ResetSpawnStateForPlanetTravel()
        {
            spawnedObjects.Clear();
            spawnedThisSession = false;
        }

        private void Awake()
        {
            CachePlanetEnvironment();
        }

        private void OnEnable()
        {
            TrySpawnNow();

            // Cover the case where the planet is active but the server hasn't started yet.
            var nm = NetworkManager.Singleton;
            if (nm == null || spawnedThisSession) return;
            subscribedManager = nm;
            nm.OnServerStarted += HandleServerStarted;
            nm.OnServerStopped += HandleServerStopped;
        }

        private void OnDisable()
        {
            UnsubscribeFromNetworkManager();
            CleanupSpawnedObjects(resetSession: true);
        }

        private void OnDestroy()
        {
            UnsubscribeFromNetworkManager();
            CleanupSpawnedObjects(resetSession: false);
        }

        private void HandleServerStarted()
        {
            TrySpawnNow();
        }

        private void TrySpawnNow()
        {
            if (spawnedThisSession) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer) return;
            if (lootPool == null || spawnPoints == null || spawnPoints.Length == 0) return;
            if (!IsCurrentActivePlanet()) return;

            var ownerScene = gameObject.scene;
            if (!ownerScene.IsValid() || !ownerScene.isLoaded)
            {
                WaitForLoadedScene(ownerScene);
                return;
            }

            spawnedThisSession = true;
            var spawnedThisPass = new List<GameObject>();
            for (var i = 0; i < spawnPoints.Length; i++)
            {
                var spawn = spawnPoints[i];
                if (spawn == null) continue;

                for (var roll = 0; roll < rollsPerSpawnPoint; roll++)
                {
                    var prefab = lootPool.Roll();
                    if (prefab == null) continue;

                    var pos = spawn.position + spawn.up * spawnSurfaceLift;
                    var rot = spawn.rotation;
                    var loot = Instantiate(prefab, pos, rot);
                    SceneManager.MoveGameObjectToScene(loot.gameObject, ownerScene);
                    loot.ServerSetSpawnPose(pos, rot);
                    if (loot.NetworkObject != null)
                    {
                        SpawnInScene(loot.NetworkObject, ownerScene);
                        SceneManager.MoveGameObjectToScene(loot.gameObject, ownerScene);
                        spawnedObjects.Add(loot.NetworkObject);
                        spawnedThisPass.Add(loot.gameObject);
                    }
                }
            }

            StartCoroutine(KeepSpawnedObjectsInScene(spawnedThisPass, ownerScene));
        }

        private void HandleServerStopped(bool wasHost)
        {
            CleanupSpawnedObjects(resetSession: true);
        }

        private void UnsubscribeFromNetworkManager()
        {
            if (subscribedManager == null) return;
            subscribedManager.OnServerStarted -= HandleServerStarted;
            subscribedManager.OnServerStopped -= HandleServerStopped;
            subscribedManager = null;
        }

        private bool IsCurrentActivePlanet()
        {
            CachePlanetEnvironment();
            if (planetEnvironment == null) return true;
            var rm = RoundManagerRegistry.Current;
            return rm != null && rm.IsEnvironmentActiveForCurrentPlanet(planetEnvironment);
        }

        private void CachePlanetEnvironment()
        {
            if (planetEnvironment != null) return;
            planetEnvironment = GetComponent<PlanetEnvironment>();
            if (planetEnvironment == null)
                planetEnvironment = GetComponentInParent<PlanetEnvironment>(true);
        }

        private void CleanupSpawnedObjects(bool resetSession)
        {
            for (var i = 0; i < spawnedObjects.Count; i++)
            {
                var obj = spawnedObjects[i];
                if (obj == null) continue;
                if (obj.IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                    obj.Despawn(destroy: true);
                else
                    Destroy(obj.gameObject);
            }
            spawnedObjects.Clear();
            if (resetSession)
                spawnedThisSession = false;
        }

        private void WaitForLoadedScene(Scene scene)
        {
            if (waitingForLoadedScene) return;
            waitingForLoadedScene = true;
            StartCoroutine(TrySpawnWhenSceneLoaded(scene));
        }

        private IEnumerator TrySpawnWhenSceneLoaded(Scene scene)
        {
            for (var frame = 0; frame < 600; frame++)
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    waitingForLoadedScene = false;
                    TrySpawnNow();
                    yield break;
                }

                yield return null;
            }

            waitingForLoadedScene = false;
        }

        private static void SpawnInScene(NetworkObject networkObject, Scene targetScene)
        {
            var previousActiveScene = SceneManager.GetActiveScene();
            var changedActiveScene = targetScene.IsValid()
                                     && targetScene.isLoaded
                                     && previousActiveScene != targetScene;

            if (changedActiveScene)
                SceneManager.SetActiveScene(targetScene);

            try
            {
                networkObject.Spawn(destroyWithScene: true);
            }
            finally
            {
                if (changedActiveScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private static IEnumerator KeepSpawnedObjectsInScene(IReadOnlyList<GameObject> objects, Scene targetScene)
        {
            if (!targetScene.IsValid() || !targetScene.isLoaded || objects == null)
                yield break;

            for (var frame = 0; frame < 30; frame++)
            {
                for (var i = 0; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    if (obj != null && obj.scene != targetScene)
                        SceneManager.MoveGameObjectToScene(obj, targetScene);
                }

                yield return null;
            }
        }
    }
}
