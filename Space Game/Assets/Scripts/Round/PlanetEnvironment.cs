using System.Collections.Generic;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Round
{
    [DisallowMultipleComponent]
    public class PlanetEnvironment : MonoBehaviour
    {
        // All environments in the scene, including those on disabled GameObjects.
        // Populated once at scene load so ApplyActivePlanetEnvironment can find and enable
        // planets whose roots are currently inactive.
        public static readonly List<PlanetEnvironment> AllEnvironments = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CollectAllEnvironments()
        {
            AllEnvironments.Clear();
            AllEnvironments.AddRange(
                FindObjectsByType<PlanetEnvironment>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        public static readonly List<PlanetEnvironment> ActiveEnvironments = new();

        [SerializeField] private PlanetDefinition planet;
        [SerializeField] private LaunchpadZone launchpadZone;
        [SerializeField] private Transform[] playerSpawnPoints;
        // For legacy planets whose content root is a separate scene object (not a child of this
        // GameObject), point this at that root so transitions can enable/disable the visuals.
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private SphereWorld sphereWorld;

        [Header("Surface Snap Offsets")]
        [SerializeField, Min(0f)] private float launchpadSurfaceOffset = 0.12f;
        [SerializeField, Min(0f)] private float spawnSurfaceOffset = 0.25f;

        public PlanetDefinition Planet => planet;
        public Transform[] PlayerSpawnPoints => playerSpawnPoints;
        public LaunchpadZone LaunchpadZone => launchpadZone;
        public GameObject ContentRoot => contentRoot != null ? contentRoot : gameObject;
        public SphereWorld SphereWorld => sphereWorld;

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
            if (planetDefinition == null) return null;
            for (var i = 0; i < ActiveEnvironments.Count; i++)
            {
                var env = ActiveEnvironments[i];
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
