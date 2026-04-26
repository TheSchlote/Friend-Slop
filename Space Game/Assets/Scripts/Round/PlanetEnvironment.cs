using System.Collections.Generic;
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

        public PlanetDefinition Planet => planet;
        public Transform[] PlayerSpawnPoints => playerSpawnPoints;
        public LaunchpadZone LaunchpadZone => launchpadZone;
        public GameObject ContentRoot => contentRoot != null ? contentRoot : gameObject;

        private void OnEnable()
        {
            if (!ActiveEnvironments.Contains(this))
                ActiveEnvironments.Add(this);
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
    }
}
