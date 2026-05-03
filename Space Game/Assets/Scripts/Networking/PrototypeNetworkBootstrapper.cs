using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Loot;
using FriendSlop.Player;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using FriendSlop.Ship;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Networking
{
    public partial class PrototypeNetworkBootstrapper : MonoBehaviour
    {
        [SerializeField] private RoundManager roundManagerPrefab;
        [SerializeField] private NetworkSceneTransitionService sceneTransitionService;
        [SerializeField] private Transform[] playerSpawnPoints;
        [SerializeField] private Transform[] shipSpawnPoints;
        [SerializeField] private NetworkLootItem[] lootPrefabs;
        // Legacy fallback. Once each planet lives in its own scene, leave this empty and
        // wire spawn anchors on PlanetEnvironment.lootSpawnPoints instead - cross-scene
        // serialized refs here would null out the moment the planet split happens.
        [SerializeField] private Transform[] lootSpawnPoints;
        [SerializeField] private RoamingMonster monsterPrefab;
        [SerializeField] private Transform[] monsterSpawnPoints;

        [Header("Tier 1 Ship Part Placement")]
        // Ship parts are placed within an angular cone around the launchpad (in degrees of
        // arc on the planet surface). Capped at 89 to guarantee same-hemisphere placement.
        [SerializeField, Range(1f, 89f)] private float shipPartMaxLaunchpadAngleDeg = 70f;
        // Inner ring keeps parts off the launchpad itself so players still have to fetch them.
        [SerializeField, Range(0f, 88f)] private float shipPartMinLaunchpadAngleDeg = 20f;
        [SerializeField, Min(0f)] private float shipPartSurfaceLift = 0.2f;

        private readonly List<NetworkObject> spawnedObjects = new();
        private NetworkManager subscribedManager;
        private bool spawnedForSession;
        // SpawnLoot/SpawnMonsters defer until the active planet's env is registered so
        // per-planet scenes (which load asynchronously) can supply spawn anchors via
        // PlanetEnvironment.
        private PlanetDefinition spawnedLootForPlanet;
        private PlanetDefinition spawnedMonstersForPlanet;
        private bool subscribedToPlanetRegistered;
        private bool subscribedToShipRegistered;
        private bool shipSceneLoadRequested;
        private RoundManager spawnedRoundManager;

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
            if (subscribedToPlanetRegistered)
            {
                PlanetEnvironment.Registered -= HandlePlanetEnvironmentRegistered;
                subscribedToPlanetRegistered = false;
            }

            if (subscribedToShipRegistered)
            {
                ShipEnvironment.Registered -= HandleShipEnvironmentRegistered;
                subscribedToShipRegistered = false;
            }

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
            BeginShipSpawnFlow();
        }

        private void BeginShipSpawnFlow()
        {
            if (!subscribedToShipRegistered)
            {
                ShipEnvironment.Registered += HandleShipEnvironmentRegistered;
                subscribedToShipRegistered = true;
            }

            RequestShipInteriorLoad();
            TrySpawnRoundManagerWhenShipReady();
        }

        private void RequestShipInteriorLoad()
        {
            if (shipSceneLoadRequested)
            {
                return;
            }

            shipSceneLoadRequested = true;

            var service = ResolveSceneTransitionService();
            var shipScene = service != null ? service.Catalog?.GetFirstByRole(GameSceneRole.ShipInterior) : null;
            if (shipScene == null || !shipScene.IsConfigured)
            {
                return;
            }

            var status = service.ServerLoadScene(shipScene);
            if (status != SceneEventProgressStatus.Started
                && status != SceneEventProgressStatus.SceneEventInProgress)
            {
                Debug.LogWarning($"PrototypeNetworkBootstrapper: could not load ship scene '{shipScene.ScenePath}' ({status}).");
            }
        }

        private void HandleShipEnvironmentRegistered(ShipEnvironment env)
        {
            if (spawnedRoundManager == null)
            {
                TrySpawnRoundManagerWhenShipReady();
                return;
            }

            ConfigureRoundManagerShipSpawnPoints(spawnedRoundManager);
        }

        private void TrySpawnRoundManagerWhenShipReady()
        {
            if (spawnedRoundManager != null)
            {
                return;
            }

            var spawns = ResolveShipSpawnPoints();
            if (!HasAnyLiveTransform(spawns) && HasConfiguredShipScene())
            {
                return;
            }

            spawnedRoundManager = SpawnRoundManager(spawns);
            if (spawnedRoundManager != null)
                BeginPlanetSpawnFlow();
        }

        private void BeginPlanetSpawnFlow()
        {
            if (!subscribedToPlanetRegistered)
            {
                PlanetEnvironment.Registered += HandlePlanetEnvironmentRegistered;
                subscribedToPlanetRegistered = true;
            }

            // The active planet may already be in memory (nested planets in the bootstrap
            // scene) - try once now so we don't wait on a registration that never comes.
            TrySpawnForActivePlanet();
        }

        private void HandlePlanetEnvironmentRegistered(PlanetEnvironment env)
        {
            TrySpawnForActivePlanet();
        }

        private void TrySpawnForActivePlanet()
        {
            var rm = RoundManagerRegistry.Current;
            if (rm == null) return;
            var planet = rm.CurrentPlanet;
            if (planet == null) return;
            var env = PlanetSceneOwnership.FindBindableEnvironment(planet);
            if (env == null) return;

            if (spawnedLootForPlanet != planet)
            {
                spawnedLootForPlanet = planet;
                SpawnLoot(env);
            }

            if (spawnedMonstersForPlanet != planet)
            {
                spawnedMonstersForPlanet = planet;
                SpawnMonsters(env);
            }
        }

        private RoundManager SpawnRoundManager(Transform[] resolvedShipSpawnPoints)
        {
            if (roundManagerPrefab == null)
            {
                Debug.LogError("RoundManager prefab is not assigned.");
                return null;
            }

            var round = Instantiate(roundManagerPrefab);
            round.ConfigureSpawnPoints(playerSpawnPoints);
            round.ConfigureShipSpawnPoints(resolvedShipSpawnPoints);
            round.ConfigureSceneTransitionService(ResolveSceneTransitionService());
            SpawnNetworkObject(round.NetworkObject);
            return round;
        }

        private void ConfigureRoundManagerShipSpawnPoints(RoundManager round)
        {
            if (round == null || subscribedManager == null || !subscribedManager.IsServer)
            {
                return;
            }

            var spawns = ResolveShipSpawnPoints();
            if (!HasAnyLiveTransform(spawns))
            {
                return;
            }

            round.ConfigureShipSpawnPoints(spawns);
            if (!RoundStateUtility.IsShipPhase(round.Phase.Value))
            {
                return;
            }

            for (var i = 0; i < NetworkFirstPersonController.ActivePlayers.Count; i++)
            {
                var player = NetworkFirstPersonController.ActivePlayers[i];
                if (player != null && player.IsSpawned)
                    round.ServerPlaceNewPlayer(player);
            }
        }

        private NetworkSceneTransitionService ResolveSceneTransitionService()
        {
            if (sceneTransitionService != null)
            {
                return sceneTransitionService;
            }

            var networkManager = subscribedManager != null ? subscribedManager : NetworkManager.Singleton;
            if (networkManager != null)
            {
                sceneTransitionService = networkManager.GetComponent<NetworkSceneTransitionService>();
            }

            return sceneTransitionService;
        }

        private bool HasConfiguredShipScene()
        {
            var service = ResolveSceneTransitionService();
            var shipScene = service != null ? service.Catalog?.GetFirstByRole(GameSceneRole.ShipInterior) : null;
            return shipScene != null && shipScene.IsConfigured;
        }

        private Transform[] ResolveShipSpawnPoints()
        {
            var ship = ShipEnvironment.Current;
            if (ship != null && HasAnyLiveTransform(ship.ShipSpawnPoints))
            {
                return ship.ShipSpawnPoints;
            }

            return shipSpawnPoints;
        }

        private static bool HasAnyLiveTransform(IReadOnlyList<Transform> transforms)
        {
            if (transforms == null) return false;
            for (var i = 0; i < transforms.Count; i++)
            {
                if (transforms[i] != null)
                    return true;
            }

            return false;
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
            foreach (var networkObject in spawnedObjects)
            {
                if (networkObject == null)
                {
                    continue;
                }

                Destroy(networkObject.gameObject);
            }

            spawnedForSession = false;
            spawnedLootForPlanet = null;
            spawnedMonstersForPlanet = null;
            spawnedRoundManager = null;
            shipSceneLoadRequested = false;
            spawnedObjects.Clear();
        }
    }
}
