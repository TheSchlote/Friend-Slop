using FriendSlop.Core;
using FriendSlop.Loot;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Networking
{
    public partial class PrototypeNetworkBootstrapper
    {
        private static readonly string[] DecorativeLaunchpadColliderNames =
        {
            "Crash Dirt Patch",
            "Launchpad Cable A",
            "Launchpad Cable B"
        };

        private void SpawnLoot(PlanetEnvironment activeEnv)
        {
            if (lootPrefabs == null) return;
            if (HasSceneOwnedLootSpawner(activeEnv)) return;

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

            // Swap active scene around the whole batch: Instantiate places objects into the
            // active scene, and NetworkObject.Spawn(destroyWithScene:true) latches its
            // SceneOriginHandle to the GameObject's scene at spawn time. With NGO scene
            // management enabled, attempting to MoveGameObjectToScene after Spawn is
            // unreliable - the cleaner contract is "be in the right scene before Spawn".
            using (new ActiveSceneScope(activeEnv.gameObject.scene))
            {
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
                    SpawnNetworkObject(loot.NetworkObject, destroyWithScene: true);
                }
            }
        }

        private static bool HasSceneOwnedLootSpawner(PlanetEnvironment activeEnv)
        {
            return activeEnv != null
                   && activeEnv.Planet != null
                   && activeEnv.Planet.HasPlanetScene
                   && activeEnv.GetComponentInChildren<PlanetLootSpawner>(true) != null;
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

            // See SpawnLoot for why the active-scene swap surrounds the batch.
            using (new ActiveSceneScope(activeEnv.gameObject.scene))
            {
                foreach (var spawnPoint in anchors)
                {
                    if (spawnPoint == null)
                    {
                        continue;
                    }

                    var monster = Instantiate(monsterPrefab, spawnPoint.position, spawnPoint.rotation);
                    SpawnNetworkObject(monster.NetworkObject, destroyWithScene: true);
                }
            }
        }

        // Sets the active scene to `target` for the lifetime of the scope, restoring the
        // previous active scene on dispose. Use around Instantiate + NetworkObject.Spawn so
        // freshly spawned objects land in `target` instead of the caller's scene. Inline
        // ref-struct so it does not allocate.
        private readonly ref struct ActiveSceneScope
        {
            private readonly Scene _previous;
            private readonly bool _changed;

            public ActiveSceneScope(Scene target)
            {
                _previous = SceneManager.GetActiveScene();
                _changed = target.IsValid() && target.isLoaded && _previous != target;
                if (_changed)
                {
                    SceneManager.SetActiveScene(target);
                }
            }

            public void Dispose()
            {
                if (_changed && _previous.IsValid() && _previous.isLoaded)
                {
                    SceneManager.SetActiveScene(_previous);
                }
            }
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
