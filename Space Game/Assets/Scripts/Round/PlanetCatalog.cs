using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Round
{
    [CreateAssetMenu(menuName = "Friend Slop/Planet Catalog", fileName = "PlanetCatalog")]
    public class PlanetCatalog : ScriptableObject
    {
        public const int MinTier = 1;
        public const int MaxTier = 10;

        [SerializeField] private List<PlanetDefinition> planets = new();

        public IReadOnlyList<PlanetDefinition> AllPlanets => planets;

        public int Count => planets != null ? planets.Count : 0;

        public List<PlanetDefinition> GetPlanetsForTier(int tier)
        {
            var list = new List<PlanetDefinition>();
            if (planets == null) return list;
            for (var i = 0; i < planets.Count; i++)
            {
                var planet = planets[i];
                if (planet != null && planet.Tier == tier)
                    list.Add(planet);
            }
            return list;
        }

        public PlanetDefinition GetFirstForTier(int tier)
        {
            if (planets == null) return null;
            for (var i = 0; i < planets.Count; i++)
            {
                var planet = planets[i];
                if (planet != null && planet.Tier == tier)
                    return planet;
            }
            return null;
        }

        public int IndexOf(PlanetDefinition planet)
        {
            if (planets == null || planet == null) return -1;
            for (var i = 0; i < planets.Count; i++)
            {
                if (planets[i] == planet) return i;
            }
            return -1;
        }

        public PlanetDefinition GetByIndex(int index)
        {
            if (planets == null || index < 0 || index >= planets.Count) return null;
            return planets[index];
        }
    }
}
