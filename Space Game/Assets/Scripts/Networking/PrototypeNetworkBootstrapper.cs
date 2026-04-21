using System.Collections.Generic;
using FriendSlop.Hazards;
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
        [SerializeField] private RoamingMonster monsterPrefab;
        [SerializeField] private Transform[] monsterSpawnPoints;

        private readonly List<NetworkObject> spawnedObjects = new();
        private NetworkManager subscribedManager;
        private bool spawnedForSession;
        private static readonly string[] DecorativeLaunchpadColliderNames =
        {
            "Crash Dirt Patch",
            "Launchpad Cable A",
            "Launchpad Cable B"
        };

        private void Awake()
        {
            DisableLaunchpadInterference();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
            DisableLaunchpadInterference();
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
            SpawnMonsters();
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

        private void SpawnMonsters()
        {
            if (monsterPrefab == null || monsterSpawnPoints == null)
            {
                return;
            }

            foreach (var spawnPoint in monsterSpawnPoints)
            {
                if (spawnPoint == null)
                {
                    continue;
                }

                var monster = Instantiate(monsterPrefab, spawnPoint.position, spawnPoint.rotation);
                SpawnNetworkObject(monster.NetworkObject);
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

        private static void DisableLaunchpadInterference()
        {
            var launchpadRoot = GameObject.Find("Launchpad Assembly Site");
            if (launchpadRoot != null)
            {
                foreach (var collider in launchpadRoot.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null || collider.isTrigger || collider.GetComponent<LaunchpadZone>() != null)
                    {
                        continue;
                    }

                    collider.enabled = false;
                }
            }

            foreach (var objectName in DecorativeLaunchpadColliderNames)
            {
                var target = GameObject.Find(objectName);
                if (target == null)
                {
                    continue;
                }

                foreach (var collider in target.GetComponentsInChildren<Collider>(true))
                {
                    if (collider != null && !collider.isTrigger)
                    {
                        collider.enabled = false;
                    }
                }
            }
        }
    }
}
