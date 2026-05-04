using System.Collections;
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
            AssertBootstrapDoesNotOwnShipInterior();
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
            AssertLaunchpadLayout();
            AssertShipPartSpawnPointsAreNearLaunchpadHemisphere();

            var firstLootCount = Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            var firstMonsterCount = Object.FindObjectsByType<RoamingMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
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
            Assert.AreEqual(firstLootCount, Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Restarting the host should respawn the same amount of loot without duplicates.");
            Assert.AreEqual(firstMonsterCount, Object.FindObjectsByType<RoamingMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                "Restarting the host should respawn the same amount of monsters without duplicates.");

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
            Assert.Fail("Active planet PlanetEnvironment should register within a few seconds of host start.");
        }

        // Bootstrapper defers loot/monster spawn until the active planet's env carries
        // anchors. With additive scene loads the env appears a frame or two after the
        // scene loads, so we poll for a non-zero count rather than asserting immediately.
        private static IEnumerator WaitForActivePlanetSpawnsPopulated()
        {
            for (var frame = 0; frame < 600; frame++)
            {
                var lootCount = Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                var monsterCount = Object.FindObjectsByType<RoamingMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                if (lootCount > 0 && monsterCount > 0) yield break;
                yield return null;
            }
            Assert.Fail("Bootstrapper should spawn loot and monsters once the active planet env registers.");
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
            var lootItems = Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.Greater(lootItems.Length, 0, "Host startup should spawn loot before validating scene ownership.");
            foreach (var loot in lootItems)
            {
                Assert.AreEqual(activePlanetScene.path, loot.gameObject.scene.path,
                    $"Loot '{loot.name}' should spawn in the active planet scene, not the bootstrap scene.");
            }

            var monsters = Object.FindObjectsByType<RoamingMonster>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.Greater(monsters.Length, 0, "Host startup should spawn monsters before validating scene ownership.");
            foreach (var monster in monsters)
            {
                Assert.AreEqual(activePlanetScene.path, monster.gameObject.scene.path,
                    $"Monster '{monster.name}' should spawn in the active planet scene, not the bootstrap scene.");
            }
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
            var operation = SceneManager.LoadSceneAsync("FriendSlopPrototype", LoadSceneMode.Single);
            while (operation != null && !operation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;
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

            for (var frame = 0; frame < 10; frame++)
            {
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

        private static void AssertConnectedMenuLayoutDoesNotOverlap()
        {
            var joinCodePanel = FindActiveRect("JoinCodePanel");
            var copyButton = FindActiveRect("Copy Code");
            var lobbyQueue = FindActiveRect("LobbyQueue");

            Assert.IsFalse(GetWorldRect(joinCodePanel).Overlaps(GetWorldRect(lobbyQueue)),
                "The join-code panel should not overlap the lobby queue.");
            Assert.IsFalse(GetWorldRect(copyButton).Overlaps(GetWorldRect(lobbyQueue)),
                "The copy-code button should not overlap the lobby queue.");
        }

        private static void AssertLaunchpadLayout()
        {
            var pad = GameObject.Find("Part Launchpad");
            var sign = GameObject.Find("Launchpad Sign");
            Assert.IsNotNull(pad, "Launchpad should exist in the prototype scene.");
            Assert.IsNotNull(sign, "Launchpad sign should exist in the prototype scene.");

            var signOffset = sign.transform.position - pad.transform.position;
            Assert.Less(Vector3.ProjectOnPlane(signOffset, pad.transform.up).magnitude, 0.75f,
                "Launchpad sign should stay centered over the launchpad.");
            Assert.Greater(Vector3.Dot(signOffset, pad.transform.up), 3.5f,
                "Launchpad sign should sit above the launchpad, not beside it.");

            var billboard = sign.GetComponent<WorldTextBillboard>();
            Assert.IsNotNull(billboard, "Launchpad sign should billboard toward the local player's camera.");
            var cameraProbe = new GameObject("Launchpad Sign Camera Probe").transform;
            cameraProbe.position = sign.transform.position - pad.transform.up * 3f - pad.transform.forward * 6f;
            cameraProbe.rotation = Quaternion.LookRotation(sign.transform.position - cameraProbe.position, pad.transform.up);
            billboard.ApplyForCamera(cameraProbe);
            var toCamera = (cameraProbe.position - sign.transform.position).normalized;
            Assert.Greater(Vector3.Dot(-sign.transform.forward, toCamera), 0.99f,
                "Launchpad sign TextMesh front side should face the camera so glyphs are readable.");
            Object.Destroy(cameraProbe.gameObject);

            AssertLaunchpadChildNearPad("Left Empty Wing Mount", pad, 2.25f);
            AssertLaunchpadChildNearPad("Right Empty Wing Mount", pad, 2.25f);
            AssertLaunchpadChildNearPad("Empty Engine Mount", pad, 1.25f);
        }

        private static void AssertLaunchpadChildNearPad(string objectName, GameObject pad, float maxPlanarDistance)
        {
            var child = GameObject.Find(objectName);
            Assert.IsNotNull(child, $"{objectName} should exist in the prototype scene.");
            var offset = child.transform.position - pad.transform.position;
            Assert.LessOrEqual(Vector3.ProjectOnPlane(offset, pad.transform.up).magnitude, maxPlanarDistance,
                $"{objectName} should stay grouped on the launchpad.");
        }

        private static void AssertShipPartSpawnPointsAreNearLaunchpadHemisphere()
        {
            AssertSpawnPointInNorthernHemisphere("Cockpit Nosecone Spawn");
            AssertSpawnPointInNorthernHemisphere("Bent Rocket Wings Spawn");
            AssertSpawnPointInNorthernHemisphere("Coughing Engine Spawn");
        }

        private static void AssertShipInteriorLayout()
        {
            var ship = GameObject.Find("Bigger-On-The-Inside Ship Interior");
            Assert.IsNotNull(ship, "A dev ship interior should exist in the loaded ShipInterior scene.");
            Assert.IsNotNull(ship.GetComponent<FlatGravityVolume>(), "Ship interior should use flat gravity instead of planet snapping.");

            var env = ship.GetComponent<ShipEnvironment>();
            Assert.IsNotNull(env, "Ship interior should expose a ShipEnvironment contract.");
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

            var ship = GameObject.Find("Bigger-On-The-Inside Ship Interior");
            Assert.IsNotNull(ship, "ShipInterior should stay loaded while players are on a planet.");

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

        private static RectTransform FindActiveRect(string objectName)
        {
            var target = GameObject.Find(objectName);
            Assert.IsNotNull(target, $"{objectName} should be active in the connected menu.");
            var rect = target.GetComponent<RectTransform>();
            Assert.IsNotNull(rect, $"{objectName} should have a RectTransform.");
            return rect;
        }

        private static Rect GetWorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = corners[0];
            var max = corners[0];
            for (var i = 1; i < corners.Length; i++)
            {
                min = Vector3.Min(min, corners[i]);
                max = Vector3.Max(max, corners[i]);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
