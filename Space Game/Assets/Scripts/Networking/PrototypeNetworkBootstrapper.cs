using System.Collections.Generic;
using FriendSlop.Core;
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
        private bool spawnedLootForActivePlanet;
        private bool spawnedMonstersForActivePlanet;
        private bool subscribedToPlanetRegistered;
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
            if (subscribedToPlanetRegistered)
            {
                PlanetEnvironment.Registered -= HandlePlanetEnvironmentRegistered;
                subscribedToPlanetRegistered = false;
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
            SpawnRoundManager();
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
            var rm = RoundManager.Instance;
            if (rm == null) return;
            var planet = rm.CurrentPlanet;
            if (planet == null) return;
            var env = PlanetEnvironment.FindFor(planet);
            if (env == null) return;

            if (!spawnedLootForActivePlanet)
            {
                spawnedLootForActivePlanet = true;
                SpawnLoot(env);
            }

            if (!spawnedMonstersForActivePlanet)
            {
                spawnedMonstersForActivePlanet = true;
                SpawnMonsters(env);
            }
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
            round.ConfigureShipSpawnPoints(shipSpawnPoints);
            SpawnNetworkObject(round.NetworkObject);
        }

        private void SpawnLoot(PlanetEnvironment activeEnv)
        {
            if (lootPrefabs == null) return;

            // Prefer per-planet anchors carried on the env; fall back to the legacy
            // bootstrapper array for any planets still hosted nested in this scene.
            var hasPlanetAnchors = activeEnv != null
                                   && activeEnv.LootSpawnPoints != null
                                   && activeEnv.LootSpawnPoints.Length > 0;
            if (!hasPlanetAnchors && activeEnv != null && activeEnv.Planet != null && activeEnv.Planet.HasPlanetScene)
                return;

            var anchors = hasPlanetAnchors ? activeEnv.LootSpawnPoints : lootSpawnPoints;
            if (anchors == null) return;

            // Resolve once - all ship parts roll positions relative to the same launchpad.
            // Prefer the active planet's launchpad over a stale named lookup so split-scene
            // planets work even if multiple launchpads are loaded.
            var launchpadTransform = activeEnv != null && activeEnv.LaunchpadZone != null
                ? activeEnv.LaunchpadZone.transform
                : FindLaunchpadTransform();
            var launchpadWorld = launchpadTransform != null
                ? SphereWorld.GetClosest(launchpadTransform.position)
                : null;

            var count = Mathf.Min(lootPrefabs.Length, anchors.Length);
            for (var i = 0; i < count; i++)
            {
                var prefab = lootPrefabs[i];
                var spawnPoint = anchors[i];
                if (prefab == null || spawnPoint == null)
                {
                    continue;
                }

                Vector3 pos;
                Quaternion rot;
                // Ship parts ignore their authored spawn point and instead drop within an
                // angular cone around the launchpad on the same hemisphere - keeps the rocket
                // assembly fetch quest tight on the first planet.
                if (prefab.IsShipPart && launchpadTransform != null && launchpadWorld != null)
                {
                    ResolveShipPartSpawnPose(launchpadTransform.position, launchpadWorld, out pos, out rot);
                }
                else
                {
                    pos = spawnPoint.position;
                    rot = spawnPoint.rotation;
                }

                var loot = Instantiate(prefab, pos, rot);
                loot.ServerSetSpawnPose(pos, rot);
                SpawnNetworkObject(loot.NetworkObject);
            }
        }

        private static Transform FindLaunchpadTransform()
        {
            var named = GameObject.Find("Part Launchpad");
            if (named != null) return named.transform;
            // Fallback for renamed/relocated launchpads: any LaunchpadZone in the scene.
            var zone = FindFirstObjectByType<LaunchpadZone>();
            return zone != null ? zone.transform : null;
        }

        private void ResolveShipPartSpawnPose(Vector3 launchpadPos, SphereWorld world, out Vector3 pos, out Quaternion rot)
        {
            // Clamp angles so we always stay strictly inside the launchpad's hemisphere.
            var maxAngle = Mathf.Clamp(shipPartMaxLaunchpadAngleDeg, 1f, 89f);
            var minAngle = Mathf.Clamp(Mathf.Min(shipPartMinLaunchpadAngleDeg, maxAngle - 1f), 0f, 89f);
            var launchpadDir = world.GetUp(launchpadPos);

            // Build a tangent perpendicular to launchpadDir to use as the cone tilt axis.
            // onUnitSphere can land near-parallel; fall back to fixed axes when it does.
            var tangent = Vector3.Cross(launchpadDir, Random.onUnitSphere);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(launchpadDir, Vector3.right);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(launchpadDir, Vector3.forward);
            tangent.Normalize();

            var angle = Random.Range(minAngle, maxAngle);
            var dir = (Quaternion.AngleAxis(angle, tangent) * launchpadDir).normalized;
            pos = world.GetSurfacePoint(dir, shipPartSurfaceLift);

            // Face roughly back toward the launchpad so dropped parts read well from the path.
            var forwardHint = Vector3.ProjectOnPlane(launchpadPos - pos, dir);
            if (forwardHint.sqrMagnitude < 0.001f) forwardHint = Vector3.Cross(dir, Vector3.right);
            rot = world.GetSurfaceRotation(dir, forwardHint);
        }

        private void SpawnMonsters(PlanetEnvironment activeEnv)
        {
            if (monsterPrefab == null) return;

            var hasPlanetAnchors = activeEnv != null
                                   && activeEnv.MonsterSpawnPoints != null
                                   && activeEnv.MonsterSpawnPoints.Length > 0;
            if (!hasPlanetAnchors && activeEnv != null && activeEnv.Planet != null && activeEnv.Planet.HasPlanetScene)
                return;

            var anchors = hasPlanetAnchors ? activeEnv.MonsterSpawnPoints : monsterSpawnPoints;
            if (anchors == null) return;

            foreach (var spawnPoint in anchors)
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
            foreach (var networkObject in spawnedObjects)
            {
                if (networkObject == null)
                {
                    continue;
                }

                Destroy(networkObject.gameObject);
            }

            spawnedForSession = false;
            spawnedLootForActivePlanet = false;
            spawnedMonstersForActivePlanet = false;
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
