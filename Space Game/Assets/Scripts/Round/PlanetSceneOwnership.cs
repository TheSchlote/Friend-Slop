using UnityEngine;

namespace FriendSlop.Round
{
    public static class PlanetSceneOwnership
    {
        public static PlanetDefinition ResolveSceneOwner(PlanetDefinition planet, PlanetCatalog catalog)
        {
            if (planet == null) return null;
            if (planet.HasPlanetScene) return planet;
            if (catalog == null) return null;

            for (var i = 0; i < catalog.Count; i++)
            {
                var candidate = catalog.GetByIndex(i);
                if (candidate == null) continue;
                if (candidate == planet) continue;
                if (candidate.Tier != planet.Tier) continue;
                if (candidate.HasPlanetScene) return candidate;
            }

            return null;
        }

        public static PlanetEnvironment FindBindableEnvironment(PlanetDefinition planet)
        {
            if (planet == null) return null;

            var exact = PlanetEnvironment.FindFor(planet);
            if (exact != null) return exact;

            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env != null && env.Planet != null && env.Planet.Tier == planet.Tier && IsRoundReadyEnvironment(env))
                    return env;
            }

            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env != null && env.Planet != null && env.Planet.Tier == planet.Tier)
                    return env;
            }

            return null;
        }

        public static PlanetEnvironment FindRoundReadyEnvironment(PlanetDefinition planet)
        {
            if (planet == null) return null;

            var exact = PlanetEnvironment.FindFor(planet);
            if (exact != null)
                return IsRoundReadyEnvironment(exact) ? exact : null;

            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.AllEnvironments[i];
                if (env == null || env.Planet == null || env.Planet.Tier != planet.Tier)
                    continue;
                if (IsRoundReadyEnvironment(env))
                    return env;
            }

            return null;
        }

        public static bool IsRoundReadyEnvironment(PlanetEnvironment env)
        {
            return env != null
                   && env.LaunchpadZone != null
                   && HasAnyLiveSpawn(env.PlayerSpawnPoints);
        }

        public static bool HasAnyLiveSpawn(Transform[] spawnPoints)
        {
            if (spawnPoints == null) return false;
            for (var i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] != null)
                    return true;
            }

            return false;
        }

        public static string GetReadinessProblem(PlanetDefinition planet)
        {
            var env = FindBindableEnvironment(planet);
            if (env == null)
                return "no matching PlanetEnvironment is registered";
            if (env.LaunchpadZone == null)
                return $"PlanetEnvironment '{env.name}' has no LaunchpadZone assigned";
            if (!HasAnyLiveSpawn(env.PlayerSpawnPoints))
                return $"PlanetEnvironment '{env.name}' has no live player spawn points";
            return $"PlanetEnvironment '{env.name}' is not ready";
        }
    }
}
