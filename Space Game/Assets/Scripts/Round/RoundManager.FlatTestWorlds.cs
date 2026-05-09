using UnityEngine;

namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        // Flat-test-world planets exist only in the catalog as PlanetDefinition assets;
        // unlike the authored planets they have no scene file and no nested GameObject in
        // the prototype scene. Build their environment procedurally at Awake time so that
        // by the time the host opens Test Mode, the env is registered and ServerTransitionToPlanet
        // can find it via PlanetSceneOwnership.FindRoundReadyEnvironment.
        // Spread the placements out so multiple flat worlds (if anyone ever adds more) don't
        // overlap each other or the authored planets clustered near the origin.
        private const float FlatTestWorldXOffset = 8000f;
        private const float FlatTestWorldXStride = 200f;

        private void EnsureFlatTestWorldEnvironments()
        {
            if (planetCatalog == null) return;

            // Important: build the env at the scene root, NOT under this RoundManager. The
            // showcase instantiates loot/hazard prefabs that bring their own NetworkObjects,
            // and NGO refuses to Spawn() this RoundManager's NetworkObject if any descendant
            // is also a NetworkObject ("Spawning NetworkObjects with nested NetworkObjects is
            // not supported"). The display NetworkObjects are inert (we never Spawn them and
            // their MonoBehaviours are disabled), so leaving them un-parented is fine. The env
            // persists across host sessions; the idempotency check below avoids rebuilding it.
            var built = 0;
            for (var i = 0; i < planetCatalog.AllPlanets.Count; i++)
            {
                var planet = planetCatalog.AllPlanets[i];
                if (planet == null || !planet.IsFlatTestWorld) continue;
                if (PlanetEnvironment.FindFor(planet) != null) continue;

                var position = new Vector3(FlatTestWorldXOffset + built * FlatTestWorldXStride, 0f, 0f);
                FlatTestWorldEnvironment.Build(planet, parent: null, position);
                built++;
            }
        }
    }
}
