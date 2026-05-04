using FriendSlop.Hazards;
using FriendSlop.Loot;
using UnityEngine;

namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        private void ServerCleanupRoundActorsForPlanetTravel()
        {
            if (!IsServer) return;

            var activeEnv = PlanetSceneOwnership.FindBindableEnvironment(CurrentPlanet);

            var loot = FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < loot.Length; i++)
            {
                var item = loot[i];
                if (item != null && ShouldCleanupActorForPlanetTravel(item.gameObject, activeEnv))
                    item.ServerDespawnForPlanetTravel();
            }
            lootItems.Clear();

            var monsters = FindObjectsByType<RoamingMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < monsters.Length; i++)
            {
                var monster = monsters[i];
                if (monster == null || monster.NetworkObject == null) continue;
                if (!ShouldCleanupActorForPlanetTravel(monster.gameObject, activeEnv)) continue;

                if (monster.NetworkObject.IsSpawned)
                    monster.NetworkObject.Despawn(destroy: true);
                else
                    Destroy(monster.gameObject);
            }

            ResetPlanetLootSpawnersForTravel(activeEnv);
        }

        private static bool ShouldCleanupActorForPlanetTravel(GameObject actor, PlanetEnvironment activeEnv)
        {
            if (actor == null) return false;
            if (activeEnv == null) return true;

            var planetScene = activeEnv.gameObject.scene;
            if (!planetScene.IsValid()) return true;
            return actor.scene.handle == planetScene.handle;
        }

        private static void ResetPlanetLootSpawnersForTravel(PlanetEnvironment activeEnv)
        {
            if (activeEnv == null) return;

            var spawners = activeEnv.GetComponentsInChildren<PlanetLootSpawner>(true);
            for (var i = 0; i < spawners.Length; i++)
            {
                if (spawners[i] != null)
                    spawners[i].ResetSpawnStateForPlanetTravel();
            }
        }

        private static void TrySpawnActivePlanetLootSpawners(PlanetEnvironment activeEnv)
        {
            if (activeEnv == null) return;

            var spawners = activeEnv.GetComponentsInChildren<PlanetLootSpawner>(true);
            for (var i = 0; i < spawners.Length; i++)
            {
                if (spawners[i] != null)
                    spawners[i].TrySpawnForActivePlanet();
            }
        }
    }
}
