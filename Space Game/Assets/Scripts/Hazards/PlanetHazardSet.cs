using System;
using System.Collections.Generic;
using FriendSlop.Round;
using UnityEngine;

namespace FriendSlop.Hazards
{
    // Per-planet hazard configuration (BACKLOG §9). Lets each PlanetDefinition
    // declare its eligible hazard prefabs (anomalies, monsters) without
    // hand-editing the global AnomalySpawner / MonsterSpawner arrays. Monster
    // consumption is queued — only AnomalySpawner reads this today.
    //
    // Resolution rules (centralised in ResolveAnomalyPrefabs so both the
    // spawner and tests agree):
    //   - No active planet, or planet has no hazardSet  → spawner falls back
    //     to its serialized global prefab list.
    //   - planet.SuppressAnomalies == true             → empty list (hard
    //     suppression always wins; matches existing semantics).
    //   - planet.HazardSet present with non-empty anomalies → that list.
    //   - planet.HazardSet present but empty anomalies → empty list (this
    //     planet authored "no anomalies" via data).
    [CreateAssetMenu(menuName = "Friend Slop/Hazards/Planet Hazard Set", fileName = "HazardSet")]
    public class PlanetHazardSet : ScriptableObject
    {
        [Tooltip("Anomaly prefabs eligible to spawn on this planet. Replaces the global " +
                 "AnomalySpawner anomalyPrefabs list when set. Empty array = no anomalies.")]
        [SerializeField] private GameObject[] anomalyPrefabs = Array.Empty<GameObject>();

        [Tooltip("Monster prefabs eligible on this planet. Declared here for data-shape " +
                 "completeness; monster spawning is not yet wired to consume this list — " +
                 "queued as a follow-up to §9.")]
        [SerializeField] private GameObject[] monsterPrefabs = Array.Empty<GameObject>();

        public IReadOnlyList<GameObject> AnomalyPrefabs => anomalyPrefabs ?? Array.Empty<GameObject>();
        public IReadOnlyList<GameObject> MonsterPrefabs => monsterPrefabs ?? Array.Empty<GameObject>();

        // Pure resolver: which anomaly prefabs apply for `planet`? Static so the
        // decision is unit-testable without standing up an AnomalySpawner.
        public static IReadOnlyList<GameObject> ResolveAnomalyPrefabs(
            PlanetDefinition planet,
            IReadOnlyList<GameObject> globalDefault)
        {
            var fallback = globalDefault ?? (IReadOnlyList<GameObject>)Array.Empty<GameObject>();
            if (planet == null) return fallback;
            if (planet.SuppressAnomalies) return Array.Empty<GameObject>();
            if (planet.HazardSet == null) return fallback;
            return planet.HazardSet.AnomalyPrefabs;
        }
    }
}
