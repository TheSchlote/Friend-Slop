#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Loot;
using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using FriendSlop.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    [InitializeOnLoad]
    public static class FriendSlopSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/FriendSlopPrototype.unity";
        private const string PlayerPrefabPath = "Assets/Prefabs/FriendSlopPlayer.prefab";
        private const string RoundManagerPrefabPath = "Assets/Prefabs/RoundManager.prefab";
        private const string LootPrefabFolderPath = "Assets/Prefabs/Loot";
        private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string AutoBuildMarkerPath = "Assets/FriendSlopBuildRequested.txt";
        private const float PlanetRadius = 18f;

        static FriendSlopSceneBuilder()
        {
            EditorApplication.delayCall += TryAutoBuild;
        }

        [MenuItem("Tools/Friend Slop/Rebuild Prototype Scene")]
        public static void BuildPrototypeScene()
        {
            EnsureFolders();

            var materials = CreateMaterials();
            var playerPrefab = BuildPlayerPrefab(materials);
            var roundManagerPrefab = BuildRoundManagerPrefab();
            var lootPrefabs = BuildLootPrefabs(materials);
            var networkPrefabsList = BuildNetworkPrefabsList(playerPrefab, roundManagerPrefab.gameObject, lootPrefabs);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(SceneManager.GetSceneAt(0));

            RenderSettings.ambientLight = new Color(0.28f, 0.3f, 0.32f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.03f, 0.04f, 0.045f);
            RenderSettings.fogDensity = 0.018f;

            CreateLighting();
            CreateMenuCamera();
            CreateNetworkManager(playerPrefab, networkPrefabsList);
            var spawns = CreateLevel(materials);
            var lootSpawnPoints = CreateLootSpawnPoints();
            CreateLaunchpad(materials);
            CreateRuntimeBootstrapper(roundManagerPrefab, spawns, lootPrefabs, lootSpawnPoints);
            CreateRuntimeUi();

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            AddSceneToBuildSettings(ScenePath);

            AssetDatabase.DeleteAsset(AutoBuildMarkerPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (Application.isBatchMode)
            {
                Debug.Log("Friend Slop prototype scene generated at Assets/Scenes/FriendSlopPrototype.unity.");
            }
            else
            {
                EditorUtility.DisplayDialog("Friend Slop", "Prototype scene generated at Assets/Scenes/FriendSlopPrototype.unity.", "OK");
            }
        }

        private static void TryAutoBuild()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoBuild;
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<TextAsset>(AutoBuildMarkerPath) == null)
            {
                return;
            }

            BuildPrototypeScene();
        }

        private static void EnsureFolders()
        {
            CreateFolder("Assets", "Scripts");
            CreateFolder("Assets/Scripts", "Core");
            CreateFolder("Assets/Scripts", "Networking");
            CreateFolder("Assets/Scripts", "Player");
            CreateFolder("Assets/Scripts", "Interaction");
            CreateFolder("Assets/Scripts", "Loot");
            CreateFolder("Assets/Scripts", "Round");
            CreateFolder("Assets/Scripts", "Hazards");
            CreateFolder("Assets/Scripts", "UI");
            CreateFolder("Assets/Scripts", "Editor");
            CreateFolder("Assets", "Prefabs");
            CreateFolder("Assets/Prefabs", "Loot");
            CreateFolder("Assets", "Materials");
            CreateFolder("Assets", "Scenes");
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var materials = new Dictionary<string, Material>
            {
                ["Concrete"] = CreateMaterial("Concrete", new Color(0.45f, 0.47f, 0.45f)),
                ["DarkWall"] = CreateMaterial("DarkWall", new Color(0.18f, 0.2f, 0.2f)),
                ["SafetyYellow"] = CreateMaterial("SafetyYellow", new Color(0.95f, 0.78f, 0.12f)),
                ["Extraction"] = CreateMaterial("Extraction", new Color(0.1f, 0.8f, 0.35f)),
                ["PlanetGrass"] = CreateMaterial("PlanetGrass", new Color(0.18f, 0.62f, 0.34f)),
                ["PlanetDirt"] = CreateMaterial("PlanetDirt", new Color(0.38f, 0.28f, 0.2f)),
                ["Launchpad"] = CreateMaterial("Launchpad", new Color(0.18f, 0.18f, 0.2f)),
                ["ShipPart"] = CreateMaterial("ShipPart", new Color(0.92f, 0.92f, 0.88f)),
                ["Player"] = CreateMaterial("Player", new Color(0.1f, 0.55f, 0.9f)),
                ["Monster"] = CreateMaterial("Monster", new Color(0.85f, 0.08f, 0.06f)),
                ["LootBlue"] = CreateMaterial("LootBlue", new Color(0.15f, 0.35f, 0.95f)),
                ["LootGreen"] = CreateMaterial("LootGreen", new Color(0.15f, 0.75f, 0.45f)),
                ["LootPink"] = CreateMaterial("LootPink", new Color(0.95f, 0.22f, 0.55f)),
                ["LootMetal"] = CreateMaterial("LootMetal", new Color(0.55f, 0.58f, 0.6f)),
                ["GlowCube"] = CreateMaterial("GlowCube", new Color(0.3f, 1f, 0.95f))
            };

            return materials;
        }

        private static GameObject BuildPlayerPrefab(IReadOnlyDictionary<string, Material> materials)
        {
            var root = new GameObject("FriendSlopPlayer");
            root.tag = "Player";
            root.AddComponent<NetworkObject>();
            root.AddComponent<ClientNetworkTransform>();

            var characterController = root.AddComponent<CharacterController>();
            characterController.height = 1.78f;
            characterController.radius = 0.34f;
            characterController.center = new Vector3(0f, 0.89f, 0f);
            characterController.stepOffset = 0.32f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Remote Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.85f, 0.7f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            SetMaterial(body, materials["Player"]);

            var cameraRoot = new GameObject("Camera Root");
            cameraRoot.transform.SetParent(root.transform, false);
            cameraRoot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var cameraObject = new GameObject("First Person Camera");
            cameraObject.transform.SetParent(cameraRoot.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 76f;
            camera.nearClipPlane = 0.04f;
            cameraObject.AddComponent<AudioListener>();

            var carryAnchor = new GameObject("Carry Anchor");
            carryAnchor.transform.SetParent(cameraRoot.transform, false);
            carryAnchor.transform.localPosition = new Vector3(0f, -0.15f, 2.1f);

            var controller = root.AddComponent<NetworkFirstPersonController>();
            var interactor = root.AddComponent<PlayerInteractor>();

            var controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("playerCamera").objectReferenceValue = camera;
            controllerSo.FindProperty("cameraRoot").objectReferenceValue = cameraRoot.transform;
            controllerSo.FindProperty("carryAnchor").objectReferenceValue = carryAnchor.transform;
            var hideArray = controllerSo.FindProperty("hideForOwner");
            hideArray.arraySize = 1;
            hideArray.GetArrayElementAtIndex(0).objectReferenceValue = body.GetComponent<Renderer>();
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            var interactorSo = new SerializedObject(interactor);
            interactorSo.FindProperty("interactDistance").floatValue = 3.2f;
            interactorSo.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static RoundManager BuildRoundManagerPrefab()
        {
            var root = new GameObject("Round Manager");
            root.AddComponent<NetworkObject>();
            var round = root.AddComponent<RoundManager>();
            var serializedRound = new SerializedObject(round);
            serializedRound.FindProperty("quota").intValue = 0;
            serializedRound.FindProperty("roundLengthSeconds").floatValue = 0f;
            serializedRound.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RoundManagerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<RoundManager>();
        }

        private static NetworkLootItem[] BuildLootPrefabs(IReadOnlyDictionary<string, Material> materials)
        {
            var specs = GetLootSpecs();
            var prefabs = new NetworkLootItem[specs.Length];

            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var lootObject = GameObject.CreatePrimitive(spec.Shape);
                lootObject.name = spec.Name;
                lootObject.transform.localScale = spec.Scale;
                SetMaterial(lootObject, materials[spec.MaterialName]);

                var body = lootObject.AddComponent<Rigidbody>();
                body.mass = Mathf.Lerp(1.2f, 8f, 1f - spec.SpeedMultiplier);
                body.angularDamping = 0.15f;
                body.useGravity = false;

                lootObject.AddComponent<SphericalRigidbodyGravity>();
                lootObject.AddComponent<NetworkObject>();
                lootObject.AddComponent<NetworkTransform>();
                var loot = lootObject.AddComponent<NetworkLootItem>();
                var serializedLoot = new SerializedObject(loot);
                serializedLoot.FindProperty("itemName").stringValue = spec.Name;
                serializedLoot.FindProperty("value").intValue = spec.Value;
                serializedLoot.FindProperty("carrySpeedMultiplier").floatValue = spec.SpeedMultiplier;
                serializedLoot.FindProperty("carryDistance").floatValue = Mathf.Lerp(2.35f, 1.7f, 1f - spec.SpeedMultiplier);
                serializedLoot.FindProperty("shipPartType").enumValueIndex = (int)spec.PartType;
                serializedLoot.ApplyModifiedPropertiesWithoutUndo();

                var prefabPath = $"{LootPrefabFolderPath}/{SanitizeAssetName(spec.Name)}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(lootObject, prefabPath);
                Object.DestroyImmediate(lootObject);
                prefabs[i] = prefab.GetComponent<NetworkLootItem>();
            }

            return prefabs;
        }

        private static NetworkPrefabsList BuildNetworkPrefabsList(GameObject playerPrefab, GameObject roundManagerPrefab, IReadOnlyList<NetworkLootItem> lootPrefabs)
        {
            var prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (prefabsList == null)
            {
                prefabsList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabsList, NetworkPrefabsListPath);
            }

            var serializedList = new SerializedObject(prefabsList);
            serializedList.FindProperty("IsDefault").boolValue = true;
            var listProperty = serializedList.FindProperty("List");
            listProperty.arraySize = 0;

            AddNetworkPrefab(listProperty, playerPrefab);
            AddNetworkPrefab(listProperty, roundManagerPrefab);
            foreach (var lootPrefab in lootPrefabs)
            {
                if (lootPrefab != null)
                {
                    AddNetworkPrefab(listProperty, lootPrefab.gameObject);
                }
            }

            serializedList.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefabsList);
            return prefabsList;
        }

        private static void AddNetworkPrefab(SerializedProperty listProperty, GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            var index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            var entry = listProperty.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Override").enumValueIndex = (int)NetworkPrefabOverride.None;
            entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
            entry.FindPropertyRelative("SourceHashToOverride").uintValue = 0;
            entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
        }

        private static void CreateLighting()
        {
            var sun = new GameObject("Tiny Planet Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(38f, -28f, 0f);

            var fillPositions = new[]
            {
                new Vector3(0f, 24f, -9f),
                new Vector3(15f, 10f, 14f),
                new Vector3(-16f, 8f, 13f)
            };

            for (var i = 0; i < fillPositions.Length; i++)
            {
                var point = new GameObject($"Cheap Planet Fill Light {i + 1}");
                point.transform.position = fillPositions[i];
                var pointLight = point.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.range = 24f;
                pointLight.intensity = 1.1f;
                pointLight.color = new Color(0.88f, 0.95f, 1f);
            }
        }

        private static void CreateMenuCamera()
        {
            var cameraObject = new GameObject("Menu Camera");
            cameraObject.transform.position = new Vector3(0f, 31f, -39f);
            cameraObject.transform.LookAt(Vector3.zero);
            var camera = cameraObject.AddComponent<Camera>();
            camera.depth = -20f;
            camera.clearFlags = CameraClearFlags.Skybox;
        }

        private static void CreateNetworkManager(GameObject playerPrefab, NetworkPrefabsList networkPrefabsList)
        {
            var networkObject = new GameObject("Network Manager");
            var transport = networkObject.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

            var networkManager = networkObject.AddComponent<NetworkManager>();
            networkManager.NetworkConfig.NetworkTransport = transport;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(networkPrefabsList);
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkManager.NetworkConfig.EnableSceneManagement = false;
            networkObject.AddComponent<NetworkSessionManager>();
        }

        private static Transform[] CreateLevel(IReadOnlyDictionary<string, Material> materials)
        {
            var levelRoot = new GameObject("Tiny Sphere World").transform;

            var planetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planetObject.name = "Starter Junk Planet";
            planetObject.transform.SetParent(levelRoot, true);
            planetObject.transform.position = Vector3.zero;
            planetObject.transform.localScale = Vector3.one * PlanetRadius * 2f;
            SetMaterial(planetObject, materials["PlanetGrass"]);

            var world = planetObject.AddComponent<SphereWorld>();
            var worldSo = new SerializedObject(world);
            worldSo.FindProperty("radius").floatValue = PlanetRadius;
            worldSo.FindProperty("gravityAcceleration").floatValue = 28f;
            worldSo.ApplyModifiedPropertiesWithoutUndo();

            CreateSurfaceProp("Crash Dirt Patch", levelRoot, world, PrimitiveType.Sphere, new Vector3(0f, 1f, 0f), Vector3.forward, 0.03f, new Vector3(5.4f, 0.16f, 4.3f), materials["PlanetDirt"]);
            CreateSurfaceProp("Launchpad Cable A", levelRoot, world, PrimitiveType.Cube, new Vector3(0.18f, 0.98f, -0.08f), Vector3.forward, 0.08f, new Vector3(0.2f, 0.12f, 5.5f), materials["DarkWall"]);
            CreateSurfaceProp("Launchpad Cable B", levelRoot, world, PrimitiveType.Cube, new Vector3(-0.2f, 0.97f, 0.02f), Vector3.right, 0.08f, new Vector3(0.2f, 0.12f, 4.8f), materials["DarkWall"]);

            var rockNormals = new[]
            {
                new Vector3(0.65f, 0.68f, 0.33f),
                new Vector3(-0.72f, 0.45f, -0.28f),
                new Vector3(0.34f, -0.18f, 0.92f),
                new Vector3(-0.18f, -0.62f, -0.76f),
                new Vector3(0.84f, -0.28f, -0.18f),
                new Vector3(-0.56f, 0.12f, 0.82f)
            };

            for (var i = 0; i < rockNormals.Length; i++)
            {
                CreateSurfaceProp($"Ugly Space Rock {i + 1}", levelRoot, world, PrimitiveType.Sphere, rockNormals[i], Vector3.forward, 0.35f, new Vector3(1.4f + i * 0.12f, 0.8f, 1.1f), materials["DarkWall"]);
            }

            var spawns = new Transform[4];
            var spawnNormals = new[]
            {
                new Vector3(-0.22f, 0.96f, -0.18f),
                new Vector3(0.22f, 0.96f, -0.18f),
                new Vector3(-0.18f, 0.96f, 0.2f),
                new Vector3(0.18f, 0.96f, 0.2f)
            };

            for (var i = 0; i < spawns.Length; i++)
            {
                var spawn = new GameObject($"Player Spawn {i + 1}");
                var normal = spawnNormals[i].normalized;
                spawn.transform.position = world.GetSurfacePoint(normal, 0.25f);
                spawn.transform.rotation = world.GetSurfaceRotation(normal, Vector3.forward);
                spawns[i] = spawn.transform;
            }

            return spawns;
        }

        private static Transform[] CreateLootSpawnPoints()
        {
            var world = Object.FindFirstObjectByType<SphereWorld>();
            if (world == null)
            {
                Debug.LogError("Cannot create loot spawn points without a SphereWorld.");
                return System.Array.Empty<Transform>();
            }

            var specs = GetLootSpecs();
            var lootRoot = new GameObject("Loot Spawn Points").transform;
            var spawnPoints = new Transform[specs.Length];

            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var spawnPoint = new GameObject($"{spec.Name} Spawn");
                spawnPoint.transform.SetParent(lootRoot, true);
                PlaceOnSurface(world, spawnPoint, spec.SurfaceNormal, 0.95f, Vector3.forward);
                spawnPoints[i] = spawnPoint.transform;
            }

            return spawnPoints;
        }

        private static void CreateLaunchpad(IReadOnlyDictionary<string, Material> materials)
        {
            var world = Object.FindFirstObjectByType<SphereWorld>();
            if (world == null)
            {
                Debug.LogError("Cannot create launchpad without a SphereWorld.");
                return;
            }

            var launchpadRoot = new GameObject("Launchpad Assembly Site").transform;
            var padNormal = Vector3.up;

            var zone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            zone.name = "Part Launchpad";
            zone.transform.SetParent(launchpadRoot, true);
            PlaceOnSurface(world, zone, padNormal, 0.12f, Vector3.forward);
            zone.transform.localScale = new Vector3(4.8f, 0.08f, 4.8f);
            SetMaterial(zone, materials["Launchpad"]);
            zone.GetComponent<Collider>().isTrigger = true;
            zone.AddComponent<LaunchpadZone>();

            var rocketBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rocketBody.name = "Broken Rocket Body";
            rocketBody.transform.SetParent(launchpadRoot, true);
            PlaceOnSurface(world, rocketBody, padNormal, 1.25f, Vector3.forward);
            rocketBody.transform.localScale = new Vector3(0.75f, 1.35f, 0.75f);
            SetMaterial(rocketBody, materials["ShipPart"]);

            var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            nose.name = "Missing Cockpit Socket";
            nose.transform.SetParent(launchpadRoot, true);
            PlaceOnSurface(world, nose, new Vector3(0.04f, 1f, 0.04f), 2.75f, Vector3.forward);
            nose.transform.localScale = new Vector3(0.75f, 0.35f, 0.75f);
            SetMaterial(nose, materials["SafetyYellow"]);

            CreateSurfaceProp("Left Empty Wing Mount", launchpadRoot, world, PrimitiveType.Cube, new Vector3(-0.08f, 1f, 0.02f), Vector3.forward, 1.2f, new Vector3(1.8f, 0.15f, 0.28f), materials["SafetyYellow"]);
            CreateSurfaceProp("Right Empty Wing Mount", launchpadRoot, world, PrimitiveType.Cube, new Vector3(0.08f, 1f, 0.02f), Vector3.forward, 1.2f, new Vector3(1.8f, 0.15f, 0.28f), materials["SafetyYellow"]);
            CreateSurfaceProp("Empty Engine Mount", launchpadRoot, world, PrimitiveType.Cylinder, new Vector3(0f, 1f, -0.08f), Vector3.forward, 0.7f, new Vector3(0.55f, 0.35f, 0.55f), materials["SafetyYellow"]);

            var installedCockpit = CreateSurfaceProp("Installed Cockpit Visual", launchpadRoot, world, PrimitiveType.Sphere, new Vector3(0.04f, 1f, 0.08f), Vector3.forward, 2.9f, new Vector3(0.9f, 0.45f, 0.9f), materials["ShipPart"]);
            var installedWings = CreateSurfaceProp("Installed Wings Visual", launchpadRoot, world, PrimitiveType.Cube, new Vector3(0f, 1f, 0.02f), Vector3.right, 1.38f, new Vector3(3.6f, 0.12f, 0.55f), materials["ShipPart"]);
            var installedEngine = CreateSurfaceProp("Installed Engine Visual", launchpadRoot, world, PrimitiveType.Cylinder, new Vector3(0f, 1f, -0.1f), Vector3.forward, 0.58f, new Vector3(0.62f, 0.45f, 0.62f), materials["ShipPart"]);

            var readyBeacon = new GameObject("Rocket Ready Beacon");
            readyBeacon.transform.SetParent(launchpadRoot, true);
            readyBeacon.transform.position = world.GetSurfacePoint(new Vector3(0f, 1f, 0f), 4.35f);
            var readyLight = readyBeacon.AddComponent<Light>();
            readyLight.type = LightType.Point;
            readyLight.color = Color.green;
            readyLight.range = 10f;
            readyLight.intensity = 4f;

            installedCockpit.SetActive(false);
            installedWings.SetActive(false);
            installedEngine.SetActive(false);
            readyBeacon.SetActive(false);

            var display = launchpadRoot.gameObject.AddComponent<RocketAssemblyDisplay>();
            var displaySo = new SerializedObject(display);
            displaySo.FindProperty("cockpitVisual").objectReferenceValue = installedCockpit;
            displaySo.FindProperty("wingsVisual").objectReferenceValue = installedWings;
            displaySo.FindProperty("engineVisual").objectReferenceValue = installedEngine;
            displaySo.FindProperty("readyBeacon").objectReferenceValue = readyBeacon;
            displaySo.ApplyModifiedPropertiesWithoutUndo();

            CreateFloatingText("Launchpad Sign", "LAUNCHPAD\nNeeds cockpit, wings, engine", world, new Vector3(0.08f, 1f, -0.02f), 4.1f, Color.green);
        }

        private static void CreateMonster(IReadOnlyDictionary<string, Material> materials)
        {
            var monster = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            monster.name = "Roaming OSHA Violation";
            monster.transform.position = new Vector3(0f, 1.1f, 9f);
            monster.transform.localScale = new Vector3(1.1f, 1.35f, 1.1f);
            SetMaterial(monster, materials["Monster"]);
            monster.AddComponent<NetworkObject>();
            monster.AddComponent<NetworkTransform>();
            monster.AddComponent<RoamingMonster>();

            var lightObject = new GameObject("Panic Light");
            lightObject.transform.SetParent(monster.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Color.red;
            light.range = 7f;
            light.intensity = 2.5f;
        }

        private static void CreateRuntimeBootstrapper(RoundManager roundManagerPrefab, Transform[] playerSpawns, NetworkLootItem[] lootPrefabs, Transform[] lootSpawns)
        {
            var bootstrapObject = new GameObject("Network Runtime Bootstrapper");
            var bootstrapper = bootstrapObject.AddComponent<PrototypeNetworkBootstrapper>();
            var serializedBootstrapper = new SerializedObject(bootstrapper);

            serializedBootstrapper.FindProperty("roundManagerPrefab").objectReferenceValue = roundManagerPrefab;
            AssignObjectArray(serializedBootstrapper.FindProperty("playerSpawnPoints"), playerSpawns);
            AssignObjectArray(serializedBootstrapper.FindProperty("lootPrefabs"), lootPrefabs);
            AssignObjectArray(serializedBootstrapper.FindProperty("lootSpawnPoints"), lootSpawns);
            serializedBootstrapper.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignObjectArray<T>(SerializedProperty property, IReadOnlyList<T> values) where T : Object
        {
            property.arraySize = values?.Count ?? 0;
            if (values == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void CreateRuntimeUi()
        {
            var ui = new GameObject("Friend Slop Runtime UI");
            ui.AddComponent<FriendSlopUI>();
        }

        private static GameObject CreateSurfaceProp(string name, Transform parent, SphereWorld world, PrimitiveType shape, Vector3 surfaceNormal, Vector3 forwardHint, float heightOffset, Vector3 scale, Material material)
        {
            var prop = GameObject.CreatePrimitive(shape);
            prop.name = name;
            prop.transform.SetParent(parent, true);
            PlaceOnSurface(world, prop, surfaceNormal, heightOffset, forwardHint);
            prop.transform.localScale = scale;
            SetMaterial(prop, material);
            return prop;
        }

        private static void PlaceOnSurface(SphereWorld world, GameObject gameObject, Vector3 surfaceNormal, float heightOffset, Vector3 forwardHint)
        {
            var normal = surfaceNormal.normalized;
            gameObject.transform.position = world.GetSurfacePoint(normal, heightOffset);
            gameObject.transform.rotation = world.GetSurfaceRotation(normal, forwardHint);
        }

        private static void CreateFloatingText(string name, string content, SphereWorld world, Vector3 surfaceNormal, float heightOffset, Color color)
        {
            var normal = surfaceNormal.normalized;
            var textObject = new GameObject(name);
            textObject.transform.position = world.GetSurfacePoint(normal, heightOffset);

            var upHint = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (upHint.sqrMagnitude < 0.001f)
            {
                upHint = Vector3.ProjectOnPlane(Vector3.right, normal);
            }

            textObject.transform.rotation = Quaternion.LookRotation(normal, upHint.normalized);
            var text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 52;
            text.characterSize = 0.13f;
            text.color = color;
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, true);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            SetMaterial(cube, material);
            return cube;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var path = $"Assets/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (name == "GlowCube")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.4f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetMaterial(GameObject gameObject, Material material)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(scene => scene.path == scenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void CreateFolder(string parent, string folder)
        {
            var path = $"{parent}/{folder}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        private static LootSpec[] GetLootSpecs()
        {
            return new[]
            {
                new LootSpec("Cockpit Nosecone", 0, 0.72f, PrimitiveType.Capsule, new Vector3(-0.78f, 0.35f, 0.52f), new Vector3(0.95f, 1.25f, 0.95f), "ShipPart", ShipPartType.Cockpit),
                new LootSpec("Bent Rocket Wings", 0, 0.68f, PrimitiveType.Cube, new Vector3(0.62f, -0.18f, 0.76f), new Vector3(2.3f, 0.28f, 0.85f), "ShipPart", ShipPartType.Wings),
                new LootSpec("Coughing Engine", 0, 0.62f, PrimitiveType.Cylinder, new Vector3(-0.26f, -0.72f, -0.64f), new Vector3(0.75f, 1.25f, 0.75f), "ShipPart", ShipPartType.Engine),
                new LootSpec("Ancient Monitor", 90, 0.78f, PrimitiveType.Cube, new Vector3(-0.58f, 0.62f, -0.54f), new Vector3(1.2f, 0.8f, 0.7f), "LootBlue"),
                new LootSpec("Printer From Hell", 120, 0.68f, PrimitiveType.Cube, new Vector3(0.18f, 0.34f, -0.92f), new Vector3(1.4f, 0.7f, 1f), "LootMetal"),
                new LootSpec("Questionable Barrel", 75, 0.82f, PrimitiveType.Cylinder, new Vector3(-0.9f, -0.08f, -0.42f), new Vector3(0.9f, 1.2f, 0.9f), "LootGreen"),
                new LootSpec("Glowing Cube", 160, 0.72f, PrimitiveType.Cube, new Vector3(0.42f, 0.76f, 0.48f), new Vector3(0.9f, 0.9f, 0.9f), "GlowCube"),
                new LootSpec("Tiny Statue", 70, 0.9f, PrimitiveType.Capsule, new Vector3(0.88f, 0.32f, -0.34f), new Vector3(0.65f, 0.95f, 0.65f), "LootPink"),
                new LootSpec("Office Fan", 65, 0.88f, PrimitiveType.Cylinder, new Vector3(-0.18f, 0.88f, 0.44f), new Vector3(0.8f, 0.32f, 0.8f), "LootMetal"),
                new LootSpec("Wet Floor Sign", 45, 0.95f, PrimitiveType.Cube, new Vector3(0.96f, -0.08f, 0.26f), new Vector3(0.35f, 1.1f, 0.9f), "SafetyYellow"),
                new LootSpec("Suspicious Server", 130, 0.65f, PrimitiveType.Cube, new Vector3(-0.38f, -0.42f, 0.82f), new Vector3(1f, 1.4f, 0.9f), "LootBlue"),
                new LootSpec("Mystery Orb", 110, 0.8f, PrimitiveType.Sphere, new Vector3(0.5f, -0.76f, 0.18f), new Vector3(1f, 1f, 1f), "LootPink")
            };
        }

        private static string SanitizeAssetName(string name)
        {
            foreach (var invalidCharacter in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalidCharacter, '_');
            }

            return name.Replace(' ', '_');
        }

        private readonly struct LootSpec
        {
            public readonly string Name;
            public readonly int Value;
            public readonly float SpeedMultiplier;
            public readonly PrimitiveType Shape;
            public readonly Vector3 SurfaceNormal;
            public readonly Vector3 Scale;
            public readonly string MaterialName;
            public readonly ShipPartType PartType;

            public LootSpec(string name, int value, float speedMultiplier, PrimitiveType shape, Vector3 surfaceNormal, Vector3 scale, string materialName, ShipPartType partType = ShipPartType.None)
            {
                Name = name;
                Value = value;
                SpeedMultiplier = speedMultiplier;
                Shape = shape;
                SurfaceNormal = surfaceNormal;
                Scale = scale;
                MaterialName = materialName;
                PartType = partType;
            }
        }
    }
}
#endif
