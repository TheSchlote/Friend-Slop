namespace FriendSlop.Round
{
    public static class PlanetDisplayUtility
    {
        public static string FormatPlanetLabel(PlanetDefinition planet, PlanetCatalog catalog = null)
        {
            if (planet == null)
            {
                return "Unknown";
            }

            var owner = PlanetSceneOwnership.ResolveSceneOwner(planet, catalog);
            if (owner != null && owner != planet)
            {
                return $"{planet.DisplayName} (Tier {planet.Tier} mission on {owner.DisplayName})";
            }

            if (!planet.HasPlanetScene && planet.Objective != null)
            {
                return $"{planet.DisplayName} (Tier {planet.Tier} mission variant)";
            }

            return $"{planet.DisplayName} (Tier {planet.Tier})";
        }
    }
}
