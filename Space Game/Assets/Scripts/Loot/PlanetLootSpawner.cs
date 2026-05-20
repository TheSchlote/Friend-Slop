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

            // See PrototypeNetworkBootstrapper.Spawning.ActiveSceneScope - swap active scene
            // around the whole batch so Instantiate places clones in ownerScene and
            // NetworkObject.Spawn(destroyWithScene:true) captures it as the SceneOriginHandle.
            // MoveGameObjectToScene-after-Spawn fights NGO scene management and is unreliable.
            var previousActiveScene = SceneManager.GetActiveScene();
            var shouldRestoreActiveScene = ownerScene != previousActiveScene;
            if (shouldRestoreActiveScene)
                SceneManager.SetActiveScene(ownerScene);

            try
            {
                var budget = ResolveLootValueBudget();
                if (budget > 0)
                    SpawnByBudget(budget);
                else
                    SpawnLegacyPerPoint();
            }
            finally
            {
                if (shouldRestoreActiveScene && previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        private int ResolveLootValueBudget()
        {
            CachePlanetEnvironment();
            return planetEnvironment != null && planetEnvironment.Planet != null
                ? planetEnvironment.Planet.LootValueBudget
                : 0;
        }

        private void SpawnLegacyPerPoint()
        {
            for (var i = 0; i < spawnPoints.Length; i++)
            {
                var spawn = spawnPoints[i];
                if (spawn == null) continue;

                for (var roll = 0; roll < rollsPerSpawnPoint; roll++)
                {
                    var prefab = lootPool.Roll();
                    if (prefab == null) continue;
                    SpawnLootAt(prefab, spawn);
                }
            }
        }

        private void SpawnByBudget(int budget)
        {
            // Walk the spawn points in order, cycling, until the budget loop
            // signals it's done. Designers control density by adding more
            // points (or duplicating a point) rather than by tuning rolls.
            var validPoints = new List<Transform>(spawnPoints.Length);
            for (var i = 0; i < spawnPoints.Length; i++)
                if (spawnPoints[i] != null) validPoints.Add(spawnPoints[i]);
            if (validPoints.Count == 0) return;

            var pointIndex = 0;
            var spawnedValue = 0;
            for (var attempt = 0; attempt < LootBudget.MaxRollsSafetyCap; attempt++)
            {
                if (LootBudget.BudgetReached(spawnedValue, budget)) break;

                var prefab = lootPool.Roll();
                if (prefab == null) continue;
                if (!LootBudget.ShouldAccept(prefab.Value, spawnedValue, budget)) continue;

                var spawn = validPoints[pointIndex];
                pointIndex = (pointIndex + 1) % validPoints.Count;
                SpawnLootAt(prefab, spawn);
                spawnedValue += Mathf.Max(0, prefab.Value);
            }
        }

        private void SpawnLootAt(NetworkLootItem prefab, Transform spawn)
        {
            var pos = spawn.position + spawn.up * spawnSurfaceLift;
            var rot = spawn.rotation;
            var loot = Instantiate(prefab, pos, rot);
            loot.ServerSetSpawnPose(pos, rot);
            if (loot.NetworkObject != null)
            {
                loot.NetworkObject.Spawn(destroyWithScene: true);
                spawnedObjects.Add(loot.NetworkObject);
            }
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
            // No PlanetEnvironment in our hierarchy means we can't confirm we own the
            // active planet - stay silent rather than spawning as if we were it, so a
            // misconfigured/legacy spawner can't double-spawn loot onto another planet.
            if (planetEnvironment == null) return false;
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

    }
}
