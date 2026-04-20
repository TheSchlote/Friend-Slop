using System.Collections.Generic;
using FriendSlop.Loot;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Networking
{
    public class PrototypeNetworkBootstrapper : MonoBehaviour
    {
        [SerializeField] private RoundManager roundManagerPrefab;
        [SerializeField] private Transform[] playerSpawnPoints;
        [SerializeField] private NetworkLootItem[] lootPrefabs;
        [SerializeField] private Transform[] lootSpawnPoints;

        private readonly List<NetworkObject> spawnedObjects = new();
        private NetworkManager subscribedManager;
        private bool spawnedForSession;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
            if (subscribedManager != null && subscribedManager.IsServer)
            {
                SpawnSessionObjects();
            }
        }

        private void OnDisable()
        {
            if (subscribedManager == null)
            {
                return;
            }

            subscribedManager.OnServerStarted -= SpawnSessionObjects;
            subscribedManager.OnServerStopped -= HandleServerStopped;
            subscribedManager = null;
        }

        private void TrySubscribe()
        {
            if (subscribedManager != null || NetworkManager.Singleton == null)
            {
                return;
            }

            subscribedManager = NetworkManager.Singleton;
            subscribedManager.OnServerStarted += SpawnSessionObjects;
            subscribedManager.OnServerStopped += HandleServerStopped;
        }

        private void SpawnSessionObjects()
        {
            if (spawnedForSession || subscribedManager == null || !subscribedManager.IsServer)
            {
                return;
            }

            spawnedForSession = true;
            SpawnRoundManager();
            SpawnLoot();
        }

        private void SpawnRoundManager()
        {
            if (roundManagerPrefab == null)
            {
                Debug.LogError("RoundManager prefab is not assigned.");
                return;
            }

            var round = Instantiate(roundManagerPrefab);
            round.ConfigureSpawnPoints(playerSpawnPoints);
            SpawnNetworkObject(round.NetworkObject);
        }

        private void SpawnLoot()
        {
            if (lootPrefabs == null || lootSpawnPoints == null)
            {
                return;
            }

            var count = Mathf.Min(lootPrefabs.Length, lootSpawnPoints.Length);
            for (var i = 0; i < count; i++)
            {
                var prefab = lootPrefabs[i];
                var spawnPoint = lootSpawnPoints[i];
                if (prefab == null || spawnPoint == null)
                {
                    continue;
                }

                var loot = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
                loot.ServerSetSpawnPose(spawnPoint.position, spawnPoint.rotation);
                SpawnNetworkObject(loot.NetworkObject);
            }
        }

        private void SpawnNetworkObject(NetworkObject networkObject)
        {
            if (networkObject == null)
            {
                return;
            }

            networkObject.Spawn();
            spawnedObjects.Add(networkObject);
        }

        private void HandleServerStopped(bool wasHost)
        {
            spawnedForSession = false;
            spawnedObjects.Clear();
        }
    }
}
