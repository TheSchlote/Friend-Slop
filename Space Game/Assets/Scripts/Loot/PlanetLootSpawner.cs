using System.Collections.Generic;
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
        private NetworkManager subscribedManager;

        public LootPool LootPool => lootPool;
        public Transform[] SpawnPoints => spawnPoints;

        public void Configure(LootPool pool, Transform[] points, int rolls = 1)
        {
            lootPool = pool;
            spawnPoints = points;
            rollsPerSpawnPoint = Mathf.Max(1, rolls);
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
            // Scene unload cleanup is handled by OnDestroy. Legacy same-scene planets may
            // be disabled and re-enabled during travel, so keep their loot in place here.
        }

        private void OnDestroy()
        {
            if (subscribedManager != null)
            {
                subscribedManager.OnServerStarted -= HandleServerStarted;
                subscribedManager.OnServerStopped -= HandleServerStopped;
                subscribedManager = null;
            }

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

            spawnedThisSession = true;
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
                    var ownerScene = gameObject.scene;
                    if (ownerScene.IsValid() && ownerScene.isLoaded)
                        SceneManager.MoveGameObjectToScene(loot.gameObject, ownerScene);
                    loot.ServerSetSpawnPose(pos, rot);
                    if (loot.NetworkObject != null)
                    {
                        loot.NetworkObject.Spawn(destroyWithScene: true);
                        spawnedObjects.Add(loot.NetworkObject);
                    }
                }
            }
        }

        private void HandleServerStopped(bool wasHost)
        {
            CleanupSpawnedObjects(resetSession: true);
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
    }
}
