using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Round
{
    [DisallowMultipleComponent]
    public class PlanetEnvironment : MonoBehaviour
    {
        // Every environment that exists in any currently-loaded scene. Two registration
        // sources keep this populated:
        //   1. RuntimeInitializeOnLoadMethod after the initial scene load, which catches
        //      legacy nested envs whose GameObjects are inactive at startup (Awake doesn't
        //      run on those until they're enabled).
        //   2. Awake on each PlanetEnvironment, which catches additively-loaded planet
        //      scenes whose GameObjects are active when the scene loads. Awake's contains
        //      check makes the two sources idempotent.
        public static readonly List<PlanetEnvironment> AllEnvironments = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CollectAllEnvironments()
        {
            // Active scene loads (typically the bootstrap scene). Additive planet scenes
            // miss this and rely on Awake instead.
            AllEnvironments.Clear();
            AllEnvironments.AddRange(
                FindObjectsByType<PlanetEnvironment>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        // Subset of AllEnvironments whose GameObject is currently active. Inactive nested
        // planets in the prototype scene stay in AllEnvironments but drop out of this list
        // until RoundManager re-enables them. Once every planet lives in its own scene,
        // this collapses back into AllEnvironments.
        public static readonly List<PlanetEnvironment> ActiveEnvironments = new();

        // Fired when an environment registers (Awake) or unregisters (OnDestroy) - used by
        // the round manager to re-evaluate the active planet binding when a scene loads
        // mid-session.
        public static event System.Action<PlanetEnvironment> Registered;
        public static event System.Action<PlanetEnvironment> Unregistered;

        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private LaunchpadZone launchpadZone;
        [SerializeField] private Transform[] playerSpawnPoints;
        // Anchors used by PrototypeNetworkBootstrapper to place loot/ship parts. Per-planet
        // scenes own their own anchors here so the bootstrapper doesn't need cross-scene
        // serialized references that null out at scene-split time.
        [SerializeField] private Transform[] lootSpawnPoints;
        [SerializeField] private Transform[] monsterSpawnPoints;
        // For legacy planets whose content root is a separate scene object (not a child of this
        // GameObject), point this at that root so transitions can enable/disable the visuals.
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private SphereWorld sphereWorld;

        [Header("Surface Snap Offsets")]
        [SerializeField, Min(0f)] private float launchpadSurfaceOffset = 0.12f;
        [SerializeField, Min(0f)] private float spawnSurfaceOffset = 0.25f;

        public PlanetDefinition Planet => planet;
        public Transform[] PlayerSpawnPoints => playerSpawnPoints;
        public Transform[] LootSpawnPoints => lootSpawnPoints;
        public Transform[] MonsterSpawnPoints => monsterSpawnPoints;
        public LaunchpadZone LaunchpadZone => launchpadZone;
        public GameObject ContentRoot => contentRoot != null ? contentRoot : gameObject;
        public SphereWorld SphereWorld => sphereWorld;

        private void Awake()
        {
            if (!AllEnvironments.Contains(this))
            {
                AllEnvironments.Add(this);
                Registered?.Invoke(this);
            }
        }

        private void OnDestroy()
        {
            if (AllEnvironments.Remove(this))
                Unregistered?.Invoke(this);
            ActiveEnvironments.Remove(this);
        }

        private void OnEnable()
        {
            if (!ActiveEnvironments.Contains(this))
                ActiveEnvironments.Add(this);
            // Defensive: a planet whose sphere was resized after assets were placed would
            // otherwise leave its launchpad / spawns floating above (or buried in) the surface.
            SnapAssetsToSurface();
        }

        private void OnDisable()
        {
            ActiveEnvironments.Remove(this);
        }

        // Active = this planet hosts the current round. Inactive planets keep their
        // visuals/colliders so players can see them in the sky, but their launchpad
        // stops accepting boarders/loot so distant pads don't influence the round.
        public void SetActiveForRound(bool active)
        {
            if (launchpadZone != null)
                launchpadZone.enabled = active;
        }

        public static PlanetEnvironment FindFor(PlanetDefinition planetDefinition)
        {
            // Search AllEnvironments (populated in Awake) rather than ActiveEnvironments
            // (populated in OnEnable). The Registered event fires inside Awake, so
            // subscribers that look up the env synchronously from that callback need to
            // see registrations that haven't yet had OnEnable run.
            if (planetDefinition == null) return null;
            for (var i = 0; i < AllEnvironments.Count; i++)
            {
                var env = AllEnvironments[i];
                if (env != null && env.planet == planetDefinition)
                    return env;
            }
            return null;
        }

        public void Configure(PlanetDefinition planetDefinition, LaunchpadZone zone, Transform[] spawns)
        {
            planet = planetDefinition;
            launchpadZone = zone;
            playerSpawnPoints = spawns;
        }

        public void SetLootSpawnPoints(Transform[] points) => lootSpawnPoints = points;
        public void SetMonsterSpawnPoints(Transform[] points) => monsterSpawnPoints = points;

        public void SetSphereWorld(SphereWorld world) => sphereWorld = world;

        // Snap the launchpad, every player spawn, and any PlanetSurfaceAnchor in this
        // hierarchy back onto the SphereWorld surface, preserving each asset's direction
        // from the planet center. Idempotent: assets already at the right radius are
        // re-placed at the same position.
        public void SnapAssetsToSurface()
        {
            var world = ResolveSphereWorld();
            if (world == null) return;

            var center = world.Center;

            if (launchpadZone != null)
                SnapTransformToSurface(launchpadZone.transform, center, world, launchpadSurfaceOffset, alignRotation: true);

            if (playerSpawnPoints != null)
            {
                for (var i = 0; i < playerSpawnPoints.Length; i++)
                {
                    var spawn = playerSpawnPoints[i];
                    if (spawn != null)
                        SnapTransformToSurface(spawn, center, world, spawnSurfaceOffset, alignRotation: true);
                }
            }

            var anchors = GetComponentsInChildren<PlanetSurfaceAnchor>(true);
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor == null) continue;
                SnapTransformToSurface(anchor.transform, center, world, anchor.SurfaceOffset, anchor.AlignRotation);
            }
        }

        private static void SnapTransformToSurface(Transform target, Vector3 center, SphereWorld world, float offset, bool alignRotation)
        {
            var dir = target.position - center;
            if (dir.sqrMagnitude < 1e-6f) return;
            var normal = dir.normalized;
            target.position = world.GetSurfacePoint(normal, offset);
            if (!alignRotation) return;

            var forwardHint = Vector3.ProjectOnPlane(target.forward, normal);
            if (forwardHint.sqrMagnitude < 1e-4f)
                forwardHint = Vector3.ProjectOnPlane(Vector3.forward, normal);
            target.rotation = world.GetSurfaceRotation(normal, forwardHint);
        }

        private SphereWorld ResolveSphereWorld()
        {
            if (sphereWorld != null) return sphereWorld;
            sphereWorld = GetComponentInChildren<SphereWorld>(true);
            if (sphereWorld != null) return sphereWorld;

            // Legacy: tier 1 env wraps a separate "Tiny Sphere World" root.
            if (contentRoot != null)
                sphereWorld = contentRoot.GetComponentInChildren<SphereWorld>(true);
            return sphereWorld;
        }
    }
}
