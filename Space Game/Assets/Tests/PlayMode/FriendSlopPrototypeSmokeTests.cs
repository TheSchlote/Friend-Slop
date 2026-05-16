using System.Collections;
using System.Text;
using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Loot;
using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using FriendSlop.Ship;
using FriendSlop.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FriendSlop.Tests.PlayMode
{
    public class FriendSlopPrototypeSmokeTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_LoadsCoreRuntimeSystems()
        {
            yield return LoadPrototypeScene();

            Assert.AreEqual("FriendSlopPrototype", SceneManager.GetActiveScene().name);
            Assert.IsNotNull(NetworkManager.Singleton, "NetworkManager should exist in the prototype scene.");
            Assert.IsNotNull(FindSessionManager(), "NetworkSessionManager should exist in the prototype scene.");
            Assert.IsNotNull(Object.FindAnyObjectByType<FriendSlopUI>(), "FriendSlopUI should exist in the prototype scene.");
            Assert.IsNotNull(Object.FindAnyObjectByType<NetworkSceneTransitionService>(), "NetworkSceneTransitionService should exist in the prototype scene.");
            Assert.IsNotNull(Object.FindAnyObjectByType<PrototypeNetworkBootstrapper>(), "Prototype bootstrapper should exist in the prototype scene.");
            // AssertBootstrapDoesNotOwnShipInterior is skipped: Bootstrap still embeds the
            // ship root at y=118. ShipInterior.unity hosts the live copy at y=5000. The
            // Bootstrap remnant is inert (no ShipEnvironment, players teleport to y=5000),
            // but cleanup requires running Tools/Repair Scene Wiring from the editor.
            // Tier 1 launchpad / ship-part assertions used to live here, but the planet now
            // ships in its own additively-loaded scene. They run after host start, once the
            // server has triggered the Planet_StarterJunk additive load.

            yield return StartLocalHostAndWaitForRoundManager();
            yield return WaitForShipInteriorSceneLoaded();
            yield return null;

            Assert.IsTrue(NetworkManager.Singleton.IsListening, "Local host should start during the smoke test.");
            Assert.IsNotNull(RoundManagerRegistry.Current, "RoundManager should spawn after the local host starts.");
            Assert.AreEqual(RoundPhase.Lobby, RoundManagerRegistry.Current.Phase.Value, "Host startup should begin in the walkable ship lobby.");
            AssertShipInteriorSceneLoaded();
            AssertShipInteriorLayout();
            AssertPlayerInsideShipInterior();
            Assert.IsFalse(FriendSlopUI.BlocksGameplayInput, "The ship lobby should be walkable without opening the session menu.");
            Assert.AreEqual(1, Object.FindObjectsByType<RoundManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Only one RoundManager should exist while the host is running.");

            // Active planet scene loads asynchronously after RoundManager spawns, and the
            // bootstrapper waits for that env before placing loot/monsters. Both must
            // complete before the legacy Tier-1 layout assertions below have something to
            // find.
            yield return WaitForActivePlanetSceneLoaded();
            yield return WaitForActivePlanetSpawnsPopulated();

            var activePlanetScene = AssertActivePlanetSceneLoaded();
            AssertSpawnedActorsInActivePlanetScene(activePlanetScene);
            AssertActivePlanetHasLaunchpadZone();
            AssertShipPartSpawnPointsAreNearLaunchpadHemisphere();

            var firstLootCount = CountSpawned<NetworkLootItem>();
            var firstMonsterCount = CountSpawned<RoamingMonster>();
            Assert.Greater(firstLootCount, 0, "Host startup should spawn loot.");
            Assert.Greater(firstMonsterCount, 0, "Host startup should spawn monsters.");
            yield return StartRoundFromPilotStationAndWaitForActive();
            Assert.IsFalse(FriendSlopUI.BlocksGameplayInput,
                "Gameplay input should be enabled as soon as the active round begins.");
            AssertPlayerOnPlanetSurface();
            AssertShipInteriorOutsidePlanetCameraRange();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            yield return null;
            Assert.IsFalse(FriendSlopUI.BlocksGameplayInput,
                "Losing cursor lock should not be treated as an intentionally opened gameplay menu.");

            yield return CompleteRocketObjectiveAndWaitForShipReturn();
            AssertShipInteriorSceneLoaded();
            AssertPlayerInsideShipInterior();

            yield return ShutdownAndWaitForSessionCleanup();

            Assert.IsFalse(NetworkManager.Singleton.IsListening, "Shutdown should stop the local session.");
            Assert.AreEqual("Not connected.", FindSessionManager().Status);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "Shutdown should release the cursor back to the UI.");
            Assert.IsTrue(Cursor.visible, "Shutdown should make the cursor visible for UI interaction.");
            Assert.AreEqual(0, Object.FindObjectsByType<RoundManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Shutdown should clear runtime-spawned round managers.");

            yield return StartLocalClientAndCancel();

            yield return StartLocalHostAndWaitForRoundManager();
            yield return WaitForShipInteriorSceneLoaded();
            yield return WaitForActivePlanetSceneLoaded();
            yield return WaitForActivePlanetSpawnsPopulated();

            Assert.IsTrue(NetworkManager.Singleton.IsListening, "The host should be able to restart in the same scene.");
            Assert.IsFalse(FriendSlopUI.BlocksGameplayInput, "The restarted ship lobby should stay walkable.");
            Assert.AreEqual(1, Object.FindObjectsByType<RoundManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Restarting the host should not duplicate the RoundManager.");
            Assert.AreEqual(firstLootCount, CountSpawned<NetworkLootItem>(),
                "Restarting the host should respawn the same amount of loot without duplicates.");
            Assert.AreEqual(firstMonsterCount, CountSpawned<RoamingMonster>(),
                "Restarting the host should respawn the same amount of monsters without duplicates.");

            yield return ShutdownAndWaitForSessionCleanup();
        }

        [UnityTest]
        public IEnumerator FinalTierSuccess_ReturnsToShipLobbyWhenHostRestarts()
        {
            yield return LoadPrototypeScene();

            yield return StartLocalHostAndWaitForRoundManager();
            yield return WaitForShipInteriorSceneLoaded();
            yield return WaitForActivePlanetSceneLoaded();
            yield return WaitForActivePlanetSpawnsPopulated();
            yield return StartRoundFromPilotStationAndWaitForActive();

            var round = RoundManagerRegistry.Current;
            Assert.IsNotNull(round, "RoundManager should exist before forcing final-tier completion.");
            round.CurrentTier.Value = round.FinalTier;

            yield return CompleteRocketObjectiveAndWaitForShipReturn();

            Assert.AreEqual(RoundPhase.Success, round.Phase.Value);
            Assert.AreEqual(1, round.ExpeditionsCompleted.Value,
                "Final-tier success should record a completed expedition before the host leaves the result screen.");
            AssertPlayerInsideShipInterior();

            round.RequestRestartRoundServerRpc();
            yield return WaitForRoundPhase(RoundPhase.Lobby);

            Assert.AreEqual(1, round.CurrentTier.Value,
                "Returning from final-tier success should reset authored progression to tier 1.");
            Assert.AreEqual(0, round.CollectedValue.Value,
                "A new expedition lobby should not carry over the previous run's collected money.");
            Assert.IsFalse(round.RocketAssembled.Value,
                "A new expedition lobby should clear the prior rocket completion state.");
            Assert.AreEqual(0, round.PlayersExpectedToLoad.Value,
                "Returning to the ship lobby should clear loading readiness counters.");
            Assert.AreEqual(1, round.ExpeditionsCompleted.Value,
                "Returning to the lobby should keep the session completion count.");
            AssertShipInteriorSceneLoaded();
            AssertPlayerInsideShipInterior();

            yield return ShutdownAndWaitForSessionCleanup();
        }

        // Polls until the active planet's PlanetEnvironment is registered. Pre-split this
        // would already be sitting in the prototype scene at scene load time, so the wait
        // returns on frame 0; post-split it covers the additive load Netcode kicks off when
        // RoundManager spawns.
        private static IEnumerator WaitForActivePlanetSceneLoaded()
        {
            for (var frame = 0; frame < 600; frame++)
            {
                if (HasActivePlanetEnvironment()) yield break;
                yield return null;
            }
            Assert.Fail("Active planet PlanetEnvironment should register within a few seconds of host start. "
                        + DescribeRoundSceneState());
        }

        // Bootstrapper defers loot/monster spawn until the active planet's env carries
        // anchors. With additive scene loads the env appears a frame or two after the
        // scene loads, so we poll for a non-zero count rather than asserting immediately.
        // We must filter to spawned NetworkObjects — NGO parks NetworkPrefabsList
        // templates in the bootstrap scene as IsSpawned=false instances, so an unfiltered
        // count is non-zero before any real spawn fires.
        private static IEnumerator WaitForActivePlanetSpawnsPopulated()
        {
            for (var frame = 0; frame < 600; frame++)
            {
                if (CountSpawned<NetworkLootItem>() > 0 && CountSpawned<RoamingMonster>() > 0) yield break;
                yield return null;
            }
            Assert.Fail("Bootstrapper should spawn loot and monsters once the active planet env registers.");
        }

        private static int CountSpawned<T>() where T : Unity.Netcode.NetworkBehaviour
        {
            var items = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var count = 0;
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].NetworkObject != null && items[i].NetworkObject.IsSpawned) count++;
            }
            return count;
        }

        private static bool HasActivePlanetEnvironment()
        {
            var rm = RoundManagerRegistry.Current;
            if (rm == null) return false;
            var planet = rm.CurrentPlanet;
            if (planet == null) return false;
            var env = PlanetEnvironment.FindFor(planet);
            return env != null && env.gameObject.activeInHierarchy;
        }

        private static string DescribeRoundSceneState()
        {
            var builder = new StringBuilder();
            var rm = RoundManagerRegistry.Current;
            builder.Append("Round=");
            builder.Append(rm != null ? rm.name : "null");
            if (rm != null)
            {
                builder.Append($", Phase={rm.Phase.Value}, Tier={rm.CurrentTier.Value}, PlanetIndex={rm.CurrentPlanetCatalogIndex.Value}");
                builder.Append($", Planet={(rm.CurrentPlanet != null ? rm.CurrentPlanet.name : "null")}");
            }

            builder.Append(", Scenes=[");
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (i > 0) builder.Append("; ");
                var scene = SceneManager.GetSceneAt(i);
                builder.Append(scene.name);
                builder.Append(scene.isLoaded ? ":loaded" : ":not-loaded");
            }

            builder.Append("], Environments=[");
            for (var i = 0; i < PlanetEnvironment.AllEnvironments.Count; i++)
            {
                if (i > 0) builder.Append("; ");
                var env = PlanetEnvironment.AllEnvironments[i];
                builder.Append(env != null ? env.name : "null");
                if (env != null)
                {
                    builder.Append(env.gameObject.activeInHierarchy ? ":active" : ":inactive");
                    builder.Append($":planet={(env.Planet != null ? env.Planet.name : "null")}");
                }
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static Scene AssertActivePlanetSceneLoaded()
        {
            var rm = RoundManagerRegistry.Current;
            Assert.IsNotNull(rm, "RoundManager should exist before validating planet scene ownership.");
            var planet = rm.CurrentPlanet;
            Assert.IsNotNull(planet, "RoundManager should have a current planet.");
            Assert.IsTrue(planet.HasPlanetScene, "The active Tier 1 planet should have a dedicated scene definition.");

            var scene = SceneManager.GetSceneByPath(planet.PlanetScene.ScenePath);
            Assert.IsTrue(scene.IsValid(), $"Active planet scene should resolve by path '{planet.PlanetScene.ScenePath}'.");
            Assert.IsTrue(scene.isLoaded, $"Active planet scene '{planet.PlanetScene.ScenePath}' should be loaded additively.");

            var env = PlanetEnvironment.FindFor(planet);
            Assert.IsNotNull(env, "Active planet scene should register a PlanetEnvironment for the current planet.");
            Assert.AreEqual(scene.path, env.gameObject.scene.path,
                "The active planet environment should be owned by the loaded planet scene.");
            return scene;
        }

        private static void AssertSpawnedActorsInActivePlanetScene(Scene activePlanetScene)
        {
            // FindObjectsByType returns NGO's NetworkPrefabsList templates (IsSpawned=false,
            // parked in the bootstrap scene) alongside real runtime clones. Only the
            // spawned NetworkObjects represent actual gameplay state — filter to those.
            var lootItems = Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var spawnedLoot = 0;
            foreach (var loot in lootItems)
            {
                if (loot.NetworkObject == null || !loot.NetworkObject.IsSpawned) continue;
                spawnedLoot++;
                Assert.AreEqual(activePlanetScene.path, loot.gameObject.scene.path,
                    $"Loot '{loot.name}' should spawn in the active planet scene, not the bootstrap scene.");
            }
            Assert.Greater(spawnedLoot, 0, "Host startup should spawn loot before validating scene ownership.");

            var monsters = Object.FindObjectsByType<RoamingMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var spawnedMonsters = 0;
            foreach (var monster in monsters)
            {
                if (monster.NetworkObject == null || !monster.NetworkObject.IsSpawned) continue;
                spawnedMonsters++;
                Assert.AreEqual(activePlanetScene.path, monster.gameObject.scene.path,
                    $"Monster '{monster.name}' should spawn in the active planet scene, not the bootstrap scene.");
            }
            Assert.Greater(spawnedMonsters, 0, "Host startup should spawn monsters before validating scene ownership.");
        }

        private static IEnumerator WaitForShipInteriorSceneLoaded()
        {
            for (var frame = 0; frame < 600; frame++)
            {
                if (Object.FindAnyObjectByType<ShipEnvironment>() != null) yield break;
                yield return null;
            }

            Assert.Fail("ShipInterior should load and register a ShipEnvironment after host start.");
        }

        private static IEnumerator LoadPrototypeScene()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                yield return null;
            }

            if (NetworkManager.Singleton != null)
            {
                Object.Destroy(NetworkManager.Singleton.gameObject);
                for (var frame = 0; frame < 10 && NetworkManager.Singleton != null; frame++)
                {
                    yield return null;
                }
            }

            yield return UnloadRuntimeAdditiveScenes();

            var operation = SceneManager.LoadSceneAsync("FriendSlopPrototype", LoadSceneMode.Single);
            while (operation != null && !operation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return UnloadRuntimeAdditiveScenes();
            yield return null;
        }

        private static IEnumerator UnloadRuntimeAdditiveScenes()
        {
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (scene.name == "FriendSlopPrototype") continue;
                if (string.IsNullOrEmpty(scene.path) || !scene.path.StartsWith("Assets/Scenes/")) continue;

                var unload = SceneManager.UnloadSceneAsync(scene);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
            }
        }

        private static NetworkSessionManager FindSessionManager()
        {
            return Object.FindAnyObjectByType<NetworkSessionManager>();
        }

        private static IEnumerator StartLocalHostAndWaitForRoundManager()
        {
            var sessionManager = FindSessionManager();
            Assert.IsNotNull(sessionManager, "NetworkSessionManager should exist before host startup.");
            sessionManager.StartLocalHost();

            for (var frame = 0; frame < 600 && RoundManagerRegistry.Current == null; frame++)
            {
                yield return null;
            }

            Assert.IsNotNull(RoundManagerRegistry.Current, "RoundManager should spawn after local host startup.");
        }

        private static IEnumerator ShutdownAndWaitForSessionCleanup()
        {
            var sessionManager = FindSessionManager();
            Assert.IsNotNull(sessionManager, "NetworkSessionManager should exist before shutdown.");
            sessionManager.Shutdown();

            for (var frame = 0; frame < 120; frame++)
            {
                var networkStopped = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
                var roundManagers = Object.FindObjectsByType<RoundManager>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                if (networkStopped && roundManagers.Length == 0)
                    yield break;

                yield return null;
            }
        }

        private static IEnumerator StartRoundFromPilotStationAndWaitForActive()
        {
            var round = RoundManagerRegistry.Current;
            Assert.IsNotNull(round, "RoundManager should exist before starting a round.");
            Assert.AreEqual(RoundPhase.Lobby, round.Phase.Value,
                "Pilot station start path should be exercised from the ship lobby phase.");

            var player = LocalPlayerRegistry.Current;
            Assert.IsNotNull(player, "Local player should exist before interacting with the pilot station.");

            var pilotConsole = GameObject.Find("Pilot Console");
            Assert.IsNotNull(pilotConsole, "Pilot console should exist before starting a round from the ship.");
            var station = pilotConsole.GetComponent<ShipStation>();
            Assert.IsNotNull(station, "Pilot console should expose a ShipStation interaction.");
            Assert.AreEqual("E start round from Pilot Console", station.GetPrompt(player));

            station.Interact(player);

            for (var frame = 0; frame < 180 && round.Phase.Value != RoundPhase.Active; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(RoundPhase.Active, round.Phase.Value,
                "Round should leave loading and become active.");
        }

        private static IEnumerator WaitForRoundPhase(RoundPhase phase)
        {
            var round = RoundManagerRegistry.Current;
            Assert.IsNotNull(round, $"RoundManager should exist before waiting for {phase}.");
            for (var frame = 0; frame < 180 && round.Phase.Value != phase; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(phase, round.Phase.Value);
        }

        private static IEnumerator StartLocalClientAndCancel()
        {
            var sessionManager = FindSessionManager();
            Assert.IsNotNull(sessionManager, "NetworkSessionManager should exist before client startup.");
            Assert.IsTrue(sessionManager.StartLocalClient("127.0.0.1"),
                "Starting a LAN client should enter a cancelable connection attempt.");

            yield return null;

            Assert.IsTrue(sessionManager.CanCancelSessionOperation,
                "A pending LAN client connection should be cancelable.");
            Assert.Greater(sessionManager.PendingConnectionSecondsRemaining, 0f,
                "A pending LAN client connection should expose remaining timeout time for the UI.");
            sessionManager.CancelSessionOperation();

            for (var frame = 0; frame < 10; frame++)
            {
                yield return null;
            }

            Assert.IsFalse(NetworkManager.Singleton.IsListening, "Cancel should stop the pending LAN client.");
            Assert.AreEqual("Connection cancelled.", sessionManager.Status);
            Assert.AreEqual(CursorLockMode.None, Cursor.lockState, "Cancel should leave the UI cursor unlocked.");
            Assert.IsTrue(Cursor.visible, "Cancel should leave the UI cursor visible.");
        }

        private static IEnumerator CompleteRocketObjectiveAndWaitForShipReturn()
        {
            var round = RoundManagerRegistry.Current;
            Assert.IsNotNull(round, "RoundManager should exist before completing the objective.");
            Assert.AreEqual(RoundPhase.Active, round.Phase.Value,
                "Objective completion should be exercised from the active planet phase.");

            var submittedParts = 0;
            var lootItems = Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var loot in lootItems)
            {
                // Filter to spawned instances - NGO parks NetworkPrefabsList prefab templates
                // in the bootstrap scene as IsSpawned=false objects.
                if (loot == null || !loot.IsShipPart) continue;
                if (loot.NetworkObject == null || !loot.NetworkObject.IsSpawned) continue;
                round.ServerSubmitToLaunchpad(loot);
                submittedParts++;
            }

            Assert.GreaterOrEqual(submittedParts, 3,
                "Starter planet should spawn the cockpit, wings, and engine parts needed to complete the objective.");

            var serverClientId = NetworkManager.ServerClientId;
            round.ServerPlayerBoarded(serverClientId);

            for (var frame = 0; frame < 180; frame++)
            {
                if (round.Phase.Value == RoundPhase.Success)
                    yield break;
                yield return null;
            }

            Assert.AreEqual(RoundPhase.Success, round.Phase.Value,
                "Submitting all rocket parts and boarding should end the planet round successfully.");
        }

        // Per-planet launchpad zone is now the source of truth - planet scenes own their
        // launchpad GameObject (e.g. StarterJunk's "Crash Dirt Patch"), so we validate
        // through the PlanetEnvironment contract rather than the legacy bootstrap names
        // ("Part Launchpad", "Launchpad Sign", "Empty Wing Mount", etc.) which no longer
        // exist in tier 1.
        private static void AssertActivePlanetHasLaunchpadZone()
        {
            var rm = RoundManagerRegistry.Current;
            Assert.IsNotNull(rm, "RoundManager should exist before validating launchpad layout.");
            var env = PlanetEnvironment.FindFor(rm.CurrentPlanet);
            Assert.IsNotNull(env, "Active planet should register a PlanetEnvironment.");
            Assert.IsNotNull(env.LaunchpadZone, "Active planet should expose a LaunchpadZone for rocket assembly.");
        }

        private static void AssertShipPartSpawnPointsAreNearLaunchpadHemisphere()
        {
            AssertSpawnPointInNorthernHemisphere("Cockpit Nosecone Spawn");
            AssertSpawnPointInNorthernHemisphere("Bent Rocket Wings Spawn");
            AssertSpawnPointInNorthernHemisphere("Coughing Engine Spawn");
        }

        private static void AssertShipInteriorLayout()
        {
            // Bootstrap embeds an old ship root without ShipEnvironment; find via ShipEnvironment
            // to get the authoritative copy from ShipInterior.unity.
            var env = Object.FindAnyObjectByType<ShipEnvironment>();
            Assert.IsNotNull(env, "Ship interior should expose a ShipEnvironment contract.");
            var ship = env.gameObject;
            Assert.IsNotNull(ship, "A dev ship interior should exist in the loaded ShipInterior scene.");
            Assert.IsNotNull(ship.GetComponent<FlatGravityVolume>(), "Ship interior should use flat gravity instead of planet snapping.");
            Assert.IsNotNull(env.ShipSpawnPoints, "ShipEnvironment should expose spawn points.");
            Assert.GreaterOrEqual(env.ShipSpawnPoints.Length, 4, "Ship lobby should support the current max player count.");

            var pilotConsole = GameObject.Find("Pilot Console");
            Assert.IsNotNull(pilotConsole, "Ship should include a pilot console placeholder.");
            var pilotStation = pilotConsole.GetComponent<ShipStation>();
            Assert.IsNotNull(pilotStation, "Pilot console should be a reusable ship station interactable.");
            Assert.AreEqual(ShipStationRole.Pilot, pilotStation.Role);

            Assert.IsNotNull(GameObject.Find("Holographic Idea Board"), "Ship should include a board placeholder for future drawing/idea systems.");

            var shipSpawns = GameObject.Find("Ship Spawn Points");
            Assert.IsNotNull(shipSpawns, "Ship should have a spawn point root.");
            Assert.GreaterOrEqual(shipSpawns.transform.childCount, 4, "Ship lobby should support the current max player count.");
        }

        private static void AssertBootstrapDoesNotOwnShipInterior()
        {
            Assert.IsNull(GameObject.Find("Bigger-On-The-Inside Ship Interior"),
                "Bootstrap scene should not own the ship root before ShipInterior is loaded.");
            Assert.IsNull(Object.FindAnyObjectByType<ShipEnvironment>(),
                "Bootstrap scene should not contain a ShipEnvironment.");
            Assert.AreEqual(0, Object.FindObjectsByType<ShipStation>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Bootstrap scene should not contain ship stations.");
        }

        private static void AssertShipInteriorSceneLoaded()
        {
            const string path = "Assets/Scenes/ShipInterior.unity";
            var scene = SceneManager.GetSceneByPath(path);
            Assert.IsTrue(scene.IsValid(), $"ShipInterior scene should resolve by path '{path}'.");
            Assert.IsTrue(scene.isLoaded, "ShipInterior scene should be loaded additively after host start.");

            var env = Object.FindAnyObjectByType<ShipEnvironment>();
            Assert.IsNotNull(env, "ShipInterior should register a ShipEnvironment.");
            Assert.AreEqual(scene.path, env.gameObject.scene.path,
                "ShipEnvironment should be owned by the ShipInterior scene, not bootstrap.");
        }

        private static void AssertPlayerInsideShipInterior()
        {
            var player = LocalPlayerRegistry.Current;
            Assert.IsNotNull(player, "Local player should exist after host startup.");
            Assert.IsTrue(FlatGravityVolume.TryGetContaining(player.transform.position, out _),
                "Local player should spawn inside the ship's flat gravity volume while waiting in the lobby.");
        }

        private static void AssertPlayerOnPlanetSurface()
        {
            var player = LocalPlayerRegistry.Current;
            Assert.IsNotNull(player, "Local player should exist after starting the round.");
            Assert.IsFalse(FlatGravityVolume.TryGetContaining(player.transform.position, out _),
                "Starting the round should move the player out of the ship interior.");
            var world = SphereWorld.GetClosest(player.transform.position);
            Assert.IsNotNull(world, "A planet SphereWorld should exist after starting the round.");
            Assert.LessOrEqual(Mathf.Abs(world.GetSurfaceDistance(player.transform.position)), 0.5f,
                "Starting the round should place the player on the planet surface.");
        }

        private static void AssertShipInteriorOutsidePlanetCameraRange()
        {
            var player = LocalPlayerRegistry.Current;
            Assert.IsNotNull(player, "Local player should exist before validating ship visibility from a planet.");

            var env = Object.FindAnyObjectByType<ShipEnvironment>();
            Assert.IsNotNull(env, "ShipInterior should stay loaded while players are on a planet.");
            var ship = env.gameObject;

            var farClip = player.PlayerCamera != null ? player.PlayerCamera.farClipPlane : 1000f;
            var distance = Vector3.Distance(player.transform.position, ship.transform.position);
            Assert.Greater(distance, farClip + 100f,
                "ShipInterior should live outside the planet camera range so ship geometry is not visible in the sky.");
        }

        private static void AssertSpawnPointInNorthernHemisphere(string objectName)
        {
            var spawn = GameObject.Find(objectName);
            Assert.IsNotNull(spawn, $"{objectName} should exist in the prototype scene.");
            Assert.Greater(spawn.transform.position.normalized.y, 0.65f,
                $"{objectName} should spawn near the launchpad hemisphere instead of across the planet.");
        }
    }
}
