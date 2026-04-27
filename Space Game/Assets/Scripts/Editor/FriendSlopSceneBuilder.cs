#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Loot;
using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using FriendSlop.SceneManagement;
using FriendSlop.Ship;
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
        private const string MonsterPrefabPath = "Assets/Prefabs/RoamingMonster.prefab";
        private const string LootPrefabFolderPath = "Assets/Prefabs/Loot";
        private const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string AutoBuildMarkerPath = "Assets/FriendSlopBuildRequested.txt";
        private const float PlanetRadius = 36f;
        private const float PlanetGravityAcceleration = 18f;
        private const float PlayerJumpVelocity = 7.2f;
        private const float PlayerGravity = 14f;
        private const float PlayerSurfaceAlignSpeed = 14f;
        private const float PlayerGroundProbeDistance = 0.22f;
        private const float PlayerTerminalFallSpeed = 18f;
        private const string ShipInteriorRootName = "Bigger-On-The-Inside Ship Interior";
        private static readonly Vector3 ShipInteriorCenter = new(0f, 118f, 0f);

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
            var monsterPrefab = BuildMonsterPrefab(materials);
            var networkPrefabsList = BuildNetworkPrefabsList(playerPrefab, roundManagerPrefab.gameObject, monsterPrefab.gameObject, lootPrefabs);

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
            var shipSpawns = CreateShipInterior(materials);
            var lootSpawnPoints = CreateLootSpawnPoints();
            var monsterSpawnPoints = CreateMonsterSpawnPoints();
            CreateLaunchpad(materials);
            CreateRuntimeBootstrapper(roundManagerPrefab, spawns, shipSpawns, lootPrefabs, lootSpawnPoints, monsterPrefab, monsterSpawnPoints);
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

        [MenuItem("Tools/Friend Slop/Repair Prototype Scene")]
        public static void RepairPrototypeScene()
        {
            RepairPrototypeSceneInternal(showDialog: !Application.isBatchMode);
        }

        public static void RepairPrototypeSceneBatch()
        {
            RepairPrototypeSceneInternal(showDialog: false);
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

        private static void TryRepairOpenPrototypeScene()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Application.isPlaying)
            {
                EditorApplication.delayCall += TryRepairOpenPrototypeScene;
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                return;
            }

            EnsureFolders();
            var materials = CreateMaterials();
            var monsterPrefab = AssetDatabase.LoadAssetAtPath<RoamingMonster>(MonsterPrefabPath) ?? BuildMonsterPrefab(materials);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) ?? BuildPlayerPrefab(materials);
            var roundManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath);
            var lootPrefabs = LoadLootPrefabs();
            BuildNetworkPrefabsList(playerPrefab, roundManagerPrefab, monsterPrefab.gameObject, lootPrefabs);
            EnsureBootstrapperLootReferences(lootPrefabs);
            EnsureBootstrapperMonsterReferences(monsterPrefab);
            EnsureSceneTransitionServiceInOpenScene();
            EnsureLaunchpadLayoutInOpenScene(materials);
            var shipSpawns = EnsureShipInteriorInOpenScene(materials);
            EnsureBootstrapperShipReferences(shipSpawns);
            EnsureOpenSceneGameplayTuning();

            if (scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }
        }

        private static void RepairPrototypeSceneInternal(bool showDialog)
        {
            EnsureFolders();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var materials = CreateMaterials();
            var monsterPrefab = BuildMonsterPrefab(materials);
            var playerPrefab = BuildPlayerPrefab(materials);
            var roundManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoundManagerPrefabPath);
            var lootPrefabs = LoadLootPrefabs();
            BuildNetworkPrefabsList(playerPrefab, roundManagerPrefab, monsterPrefab.gameObject, lootPrefabs);
            EnsureBootstrapperLootReferences(lootPrefabs);
            EnsureBootstrapperMonsterReferences(monsterPrefab);
            EnsureSceneTransitionServiceInOpenScene();
            EnsureLaunchpadLayoutInOpenScene(materials);
            var shipSpawns = EnsureShipInteriorInOpenScene(materials);
            EnsureBootstrapperShipReferences(shipSpawns);
            EnsureOpenSceneGameplayTuning();

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (showDialog)
            {
                EditorUtility.DisplayDialog("Friend Slop", "Prototype scene repaired.", "OK");
            }
            else
            {
                Debug.Log("Friend Slop prototype scene repaired.");
            }
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
            CreateFolder("Assets/Scripts", "Ship");
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
                ["ShipFloor"] = CreateMaterial("ShipFloor", new Color(0.33f, 0.36f, 0.34f)),
                ["ShipWall"] = CreateMaterial("ShipWall", new Color(0.12f, 0.15f, 0.16f)),
                ["Console"] = CreateMaterial("Console", new Color(0.08f, 0.28f, 0.38f)),
                ["Hologram"] = CreateMaterial("Hologram", new Color(0.16f, 0.92f, 0.98f)),
                ["Window"] = CreateMaterial("Window", new Color(0.04f, 0.08f, 0.12f)),
                ["WarningRed"] = CreateMaterial("WarningRed", new Color(0.82f, 0.14f, 0.1f)),
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
            var bodyRenderer = body.GetComponent<Renderer>();

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Remote Face Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.55f, 0.16f);
            head.transform.localScale = new Vector3(0.58f, 0.48f, 0.58f);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            SetMaterial(head, materials["Player"]);
            var headRenderer = head.GetComponent<Renderer>();

            var eyeLeft = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeLeft.name = "Remote Face Eye Left";
            eyeLeft.transform.SetParent(root.transform, false);
            eyeLeft.transform.localPosition = new Vector3(-0.16f, 1.64f, 0.44f);
            eyeLeft.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            Object.DestroyImmediate(eyeLeft.GetComponent<Collider>());
            SetMaterial(eyeLeft, materials["ShipPart"]);
            var eyeLeftRenderer = eyeLeft.GetComponent<Renderer>();

            var eyeRight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeRight.name = "Remote Face Eye Right";
            eyeRight.transform.SetParent(root.transform, false);
            eyeRight.transform.localPosition = new Vector3(0.16f, 1.64f, 0.44f);
            eyeRight.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
            Object.DestroyImmediate(eyeRight.GetComponent<Collider>());
            SetMaterial(eyeRight, materials["ShipPart"]);
            var eyeRightRenderer = eyeRight.GetComponent<Renderer>();

            var mouth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mouth.name = "Remote Face Mouth";
            mouth.transform.SetParent(root.transform, false);
            mouth.transform.localPosition = new Vector3(0f, 1.43f, 0.48f);
            mouth.transform.localScale = new Vector3(0.28f, 0.06f, 0.04f);
            Object.DestroyImmediate(mouth.GetComponent<Collider>());
            SetMaterial(mouth, materials["DarkWall"]);
            var mouthRenderer = mouth.GetComponent<Renderer>();

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
            controllerSo.FindProperty("jumpVelocity").floatValue = PlayerJumpVelocity;
            controllerSo.FindProperty("gravity").floatValue = PlayerGravity;
            controllerSo.FindProperty("surfaceAlignSpeed").floatValue = PlayerSurfaceAlignSpeed;
            controllerSo.FindProperty("groundProbeDistance").floatValue = PlayerGroundProbeDistance;
            controllerSo.FindProperty("terminalFallSpeed").floatValue = PlayerTerminalFallSpeed;
            var hideArray = controllerSo.FindProperty("hideForOwner");
            hideArray.arraySize = 5;
            hideArray.GetArrayElementAtIndex(0).objectReferenceValue = bodyRenderer;
            hideArray.GetArrayElementAtIndex(1).objectReferenceValue = headRenderer;
            hideArray.GetArrayElementAtIndex(2).objectReferenceValue = eyeLeftRenderer;
            hideArray.GetArrayElementAtIndex(3).objectReferenceValue = eyeRightRenderer;
            hideArray.GetArrayElementAtIndex(4).objectReferenceValue = mouthRenderer;
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

        private static RoamingMonster BuildMonsterPrefab(IReadOnlyDictionary<string, Material> materials)
        {
            var root = new GameObject("RoamingMonster");
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            var monster = root.AddComponent<RoamingMonster>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(1f, 1.2f, 1f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            SetMaterial(body, materials["Monster"]);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.85f, 0.2f);
            head.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            SetMaterial(head, materials["Monster"]);

            var eyeLeft = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeLeft.name = "Eye Left";
            eyeLeft.transform.SetParent(root.transform, false);
            eyeLeft.transform.localPosition = new Vector3(-0.18f, 1.92f, 0.48f);
            eyeLeft.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            Object.DestroyImmediate(eyeLeft.GetComponent<Collider>());
            SetMaterial(eyeLeft, materials["ShipPart"]);

            var eyeRight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eyeRight.name = "Eye Right";
            eyeRight.transform.SetParent(root.transform, false);
            eyeRight.transform.localPosition = new Vector3(0.18f, 1.92f, 0.48f);
            eyeRight.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            Object.DestroyImmediate(eyeRight.GetComponent<Collider>());
            SetMaterial(eyeRight, materials["ShipPart"]);

            var lightObject = new GameObject("Panic Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.18f, 0.12f);
            light.range = 8f;
            light.intensity = 3f;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<RoamingMonster>();
        }

        private static NetworkPrefabsList BuildNetworkPrefabsList(GameObject playerPrefab, GameObject roundManagerPrefab, GameObject monsterPrefab, IReadOnlyList<NetworkLootItem> lootPrefabs)
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
            AddNetworkPrefab(listProperty, monsterPrefab);
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

            // Positions expressed as multiples of PlanetRadius so they scale automatically.
            var s = PlanetRadius;
            var fillPositions = new[]
            {
                new Vector3(0f,    s * 1.333f, -s * 0.500f),
                new Vector3(s * 0.833f, s * 0.556f,  s * 0.778f),
                new Vector3(-s * 0.889f, s * 0.444f, s * 0.722f),
            };

            for (var i = 0; i < fillPositions.Length; i++)
            {
                var point = new GameObject($"Cheap Planet Fill Light {i + 1}");
                point.transform.position = fillPositions[i];
                var pointLight = point.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.range = s * 1.333f;
                pointLight.intensity = 1.1f;
                pointLight.color = new Color(0.88f, 0.95f, 1f);
            }

            var dayNightManager = new GameObject("Day Night Manager");
            dayNightManager.AddComponent<NetworkObject>();
            dayNightManager.AddComponent<DayNightCycle>();
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
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkObject.AddComponent<NetworkSessionManager>();
            networkObject.AddComponent<NetworkSceneTransitionService>();
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

            planetObject.AddComponent<NetworkObject>();
            planetObject.AddComponent<PlanetColorRandomizer>();
            planetObject.AddComponent<PlanetTreeSpawner>();

            var world = planetObject.AddComponent<SphereWorld>();
            var worldSo = new SerializedObject(world);
            worldSo.FindProperty("radius").floatValue = PlanetRadius;
            worldSo.FindProperty("gravityAcceleration").floatValue = PlanetGravityAcceleration;
            worldSo.ApplyModifiedPropertiesWithoutUndo();

            var dirtPatch = CreateSurfaceProp("Crash Dirt Patch", levelRoot, world, PrimitiveType.Sphere, new Vector3(0f, 1f, 0f), Vector3.forward, 0.03f, new Vector3(5.4f, 0.16f, 4.3f), materials["PlanetDirt"]);
            var cableA = CreateSurfaceProp("Launchpad Cable A", levelRoot, world, PrimitiveType.Cube, new Vector3(0.18f, 0.98f, -0.08f), Vector3.forward, 0.08f, new Vector3(0.2f, 0.12f, 5.5f), materials["DarkWall"]);
            var cableB = CreateSurfaceProp("Launchpad Cable B", levelRoot, world, PrimitiveType.Cube, new Vector3(-0.2f, 0.97f, 0.02f), Vector3.right, 0.08f, new Vector3(0.2f, 0.12f, 4.8f), materials["DarkWall"]);
            RemoveCollider(dirtPatch);
            RemoveCollider(cableA);
            RemoveCollider(cableB);

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
                PlaceOnSurface(world, spawnPoint, spec.SurfaceNormal, GetLootSurfaceOffset(spec), Vector3.forward);
                spawnPoints[i] = spawnPoint.transform;
            }

            return spawnPoints;
        }

        private static Transform[] CreateMonsterSpawnPoints()
        {
            var world = Object.FindFirstObjectByType<SphereWorld>();
            if (world == null)
            {
                Debug.LogError("Cannot create monster spawn points without a SphereWorld.");
                return System.Array.Empty<Transform>();
            }

            var spawnRoot = new GameObject("Enemy Spawn Points").transform;
            var spawnPoints = new Transform[1];
            var spawn = new GameObject("Monster Spawn 1");
            spawn.transform.SetParent(spawnRoot, true);
            PlaceOnSurface(world, spawn, new Vector3(0.08f, -1f, 0.04f), 0.7f, Vector3.forward);
            spawnPoints[0] = spawn.transform;
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
            RemoveCollider(zone);
            zone.AddComponent<LaunchpadZone>();

            var rocketBody = CreateLaunchpadProp("Broken Rocket Body", launchpadRoot, world,
                PrimitiveType.Cylinder, padNormal, Vector2.zero, 1.45f,
                Vector3.forward, new Vector3(0.75f, 1.35f, 0.75f), materials["ShipPart"]);
            RemoveCollider(rocketBody);

            var nose = CreateLaunchpadProp("Missing Cockpit Socket", launchpadRoot, world,
                PrimitiveType.Sphere, padNormal, new Vector2(0f, 0.12f), 2.95f,
                Vector3.forward, new Vector3(0.75f, 0.35f, 0.75f), materials["SafetyYellow"]);
            RemoveCollider(nose);

            var leftMount = CreateLaunchpadProp("Left Empty Wing Mount", launchpadRoot, world,
                PrimitiveType.Cube, padNormal, new Vector2(-1.05f, 0.02f), 1.42f,
                Vector3.forward, new Vector3(1.2f, 0.15f, 0.28f), materials["SafetyYellow"]);
            var rightMount = CreateLaunchpadProp("Right Empty Wing Mount", launchpadRoot, world,
                PrimitiveType.Cube, padNormal, new Vector2(1.05f, 0.02f), 1.42f,
                Vector3.forward, new Vector3(1.2f, 0.15f, 0.28f), materials["SafetyYellow"]);
            var engineMount = CreateLaunchpadProp("Empty Engine Mount", launchpadRoot, world,
                PrimitiveType.Cylinder, padNormal, new Vector2(0f, -0.62f), 0.78f,
                Vector3.forward, new Vector3(0.55f, 0.35f, 0.55f), materials["SafetyYellow"]);
            RemoveCollider(leftMount);
            RemoveCollider(rightMount);
            RemoveCollider(engineMount);

            var installedCockpit = CreateLaunchpadProp("Installed Cockpit Visual", launchpadRoot, world,
                PrimitiveType.Sphere, padNormal, new Vector2(0f, 0.12f), 3.02f,
                Vector3.forward, new Vector3(0.9f, 0.45f, 0.9f), materials["ShipPart"]);
            var installedWings = CreateLaunchpadProp("Installed Wings Visual", launchpadRoot, world,
                PrimitiveType.Cube, padNormal, new Vector2(0f, 0.02f), 1.48f,
                Vector3.forward, new Vector3(2.9f, 0.12f, 0.5f), materials["ShipPart"]);
            var installedEngine = CreateLaunchpadProp("Installed Engine Visual", launchpadRoot, world,
                PrimitiveType.Cylinder, padNormal, new Vector2(0f, -0.62f), 0.74f,
                Vector3.forward, new Vector3(0.62f, 0.45f, 0.62f), materials["ShipPart"]);
            RemoveCollider(installedCockpit);
            RemoveCollider(installedWings);
            RemoveCollider(installedEngine);

            var readyBeacon = new GameObject("Rocket Ready Beacon");
            readyBeacon.transform.SetParent(launchpadRoot, true);
            PlaceOnLaunchpad(world, readyBeacon, padNormal, Vector2.zero, 4.35f, Vector3.forward);
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

            CreateFloatingText("Launchpad Sign", "LAUNCHPAD\nBring parts here", launchpadRoot, world, padNormal, Vector2.zero, 4.6f, Color.green);
        }

        private static Transform[] CreateShipInterior(IReadOnlyDictionary<string, Material> materials)
        {
            var root = new GameObject(ShipInteriorRootName).transform;
            root.position = ShipInteriorCenter;

            var gravityVolume = root.gameObject.AddComponent<FlatGravityVolume>();
            var volumeSo = new SerializedObject(gravityVolume);
            volumeSo.FindProperty("size").vector3Value = new Vector3(30f, 10f, 22f);
            volumeSo.FindProperty("priority").intValue = 100;
            volumeSo.ApplyModifiedPropertiesWithoutUndo();

            CreateLocalCube("Ship Floor", root, new Vector3(0f, -0.08f, 0f), new Vector3(24f, 0.28f, 16f), materials["ShipFloor"]);
            CreateLocalCube("Ship Ceiling", root, new Vector3(0f, 4.4f, 0f), new Vector3(24f, 0.22f, 16f), materials["ShipWall"]);
            CreateLocalCube("Port Wall", root, new Vector3(-12.1f, 2.1f, 0f), new Vector3(0.28f, 4.4f, 16f), materials["ShipWall"]);
            CreateLocalCube("Starboard Wall", root, new Vector3(12.1f, 2.1f, 0f), new Vector3(0.28f, 4.4f, 16f), materials["ShipWall"]);
            CreateLocalCube("Aft Wall", root, new Vector3(0f, 2.1f, -8.1f), new Vector3(24f, 4.4f, 0.28f), materials["ShipWall"]);
            CreateLocalCube("Cockpit Window", root, new Vector3(0f, 2.6f, 8.05f), new Vector3(9f, 2.2f, 0.18f), materials["Window"]);
            CreateLocalCube("Cockpit Nose Wall Left", root, new Vector3(-8.3f, 2.1f, 8.1f), new Vector3(7.4f, 4.4f, 0.28f), materials["ShipWall"]);
            CreateLocalCube("Cockpit Nose Wall Right", root, new Vector3(8.3f, 2.1f, 8.1f), new Vector3(7.4f, 4.4f, 0.28f), materials["ShipWall"]);

            CreateLocalCube("Cockpit Deck Stripe", root, new Vector3(0f, 0.09f, 5.5f), new Vector3(7.6f, 0.05f, 0.35f), materials["SafetyYellow"], removeCollider: true);
            CreateLocalCube("Port Module Bay Stripe", root, new Vector3(-6.8f, 0.1f, -2.5f), new Vector3(0.35f, 0.05f, 6f), materials["WarningRed"], removeCollider: true);
            CreateLocalCube("Starboard Module Bay Stripe", root, new Vector3(6.8f, 0.1f, -2.5f), new Vector3(0.35f, 0.05f, 6f), materials["WarningRed"], removeCollider: true);

            CreateShipStation("Pilot Console", root, new Vector3(0f, 0.75f, 5.8f), new Vector3(3.2f, 1.2f, 1.6f), materials["Console"], ShipStationRole.Pilot, "Pilot Console");
            CreateShipStation("Holographic Idea Board", root, new Vector3(-11.7f, 2.1f, -1.1f), new Vector3(0.28f, 2.2f, 4.6f), materials["Hologram"], ShipStationRole.HolographicBoard, "Holographic Board");
            CreateShipStation("Minigame Module Slot A", root, new Vector3(6.8f, 0.65f, -3.7f), new Vector3(2.8f, 1.1f, 2.2f), materials["Console"], ShipStationRole.MiniGame, "Module Slot A");
            CreateShipStation("Customization Bench", root, new Vector3(-6.6f, 0.65f, -5.2f), new Vector3(3.4f, 1.1f, 1.6f), materials["ShipPart"], ShipStationRole.Customization, "Customization Bench");

            CreateLocalText("Ship Lobby Label", root, "SHIP LOBBY\nwalk, wait, cause problems", new Vector3(0f, 3.2f, -7.85f), Quaternion.Euler(0f, 0f, 0f), Color.white);
            CreateLocalText("Pilot Label", root, "PILOT", new Vector3(0f, 2.35f, 4.75f), Quaternion.Euler(58f, 0f, 0f), Color.cyan);

            var spawnRoot = new GameObject("Ship Spawn Points").transform;
            spawnRoot.SetParent(root, false);
            var spawnPositions = new[]
            {
                new Vector3(-2.4f, 0.12f, -5.6f),
                new Vector3(2.4f, 0.12f, -5.6f),
                new Vector3(-2.4f, 0.12f, -3.2f),
                new Vector3(2.4f, 0.12f, -3.2f)
            };

            var spawns = new Transform[spawnPositions.Length];
            for (var i = 0; i < spawnPositions.Length; i++)
            {
                var spawn = new GameObject($"Ship Spawn {i + 1}");
                spawn.transform.SetParent(spawnRoot, false);
                spawn.transform.localPosition = spawnPositions[i];
                spawn.transform.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                spawns[i] = spawn.transform;
            }

            return spawns;
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

        private static void CreateRuntimeBootstrapper(RoundManager roundManagerPrefab, Transform[] playerSpawns, Transform[] shipSpawns, NetworkLootItem[] lootPrefabs, Transform[] lootSpawns, RoamingMonster monsterPrefab, Transform[] monsterSpawns)
        {
            var bootstrapObject = new GameObject("Network Runtime Bootstrapper");
            var bootstrapper = bootstrapObject.AddComponent<PrototypeNetworkBootstrapper>();
            var serializedBootstrapper = new SerializedObject(bootstrapper);

            serializedBootstrapper.FindProperty("roundManagerPrefab").objectReferenceValue = roundManagerPrefab;
            AssignObjectArray(serializedBootstrapper.FindProperty("playerSpawnPoints"), playerSpawns);
            AssignObjectArray(serializedBootstrapper.FindProperty("shipSpawnPoints"), shipSpawns);
            AssignObjectArray(serializedBootstrapper.FindProperty("lootPrefabs"), lootPrefabs);
            AssignObjectArray(serializedBootstrapper.FindProperty("lootSpawnPoints"), lootSpawns);
            serializedBootstrapper.FindProperty("monsterPrefab").objectReferenceValue = monsterPrefab;
            AssignObjectArray(serializedBootstrapper.FindProperty("monsterSpawnPoints"), monsterSpawns);
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

        private static GameObject CreateLaunchpadProp(string name, Transform parent, SphereWorld world, PrimitiveType shape, Vector3 padNormal, Vector2 tangentOffset, float heightOffset, Vector3 forwardHint, Vector3 scale, Material material)
        {
            var prop = GameObject.CreatePrimitive(shape);
            prop.name = name;
            prop.transform.SetParent(parent, true);
            PlaceOnLaunchpad(world, prop, padNormal, tangentOffset, heightOffset, forwardHint);
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

        private static void PlaceOnLaunchpad(SphereWorld world, GameObject gameObject, Vector3 padNormal, Vector2 tangentOffset, float heightOffset, Vector3 forwardHint)
        {
            var normal = padNormal.normalized;
            var rotation = world.GetSurfaceRotation(normal, forwardHint);
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;
            var center = world.GetSurfacePoint(normal, heightOffset);
            gameObject.transform.SetPositionAndRotation(
                center + right * tangentOffset.x + forward * tangentOffset.y,
                rotation);
        }

        private static void CreateFloatingText(string name, string content, SphereWorld world, Vector3 surfaceNormal, float heightOffset, Color color)
        {
            CreateFloatingText(name, content, null, world, surfaceNormal, Vector2.zero, heightOffset, color);
        }

        private static void CreateFloatingText(string name, string content, SphereWorld world, Vector3 surfaceNormal, Vector2 tangentOffset, float heightOffset, Color color)
        {
            CreateFloatingText(name, content, null, world, surfaceNormal, tangentOffset, heightOffset, color);
        }

        private static void CreateFloatingText(string name, string content, Transform parent, SphereWorld world, Vector3 surfaceNormal, Vector2 tangentOffset, float heightOffset, Color color)
        {
            var normal = surfaceNormal.normalized;
            var textObject = new GameObject(name);
            if (parent != null)
            {
                textObject.transform.SetParent(parent, true);
            }

            var surfaceRotation = world.GetSurfaceRotation(normal, Vector3.forward);
            var right = surfaceRotation * Vector3.right;
            var forward = surfaceRotation * Vector3.forward;
            textObject.transform.position = world.GetSurfacePoint(normal, heightOffset)
                + right * tangentOffset.x
                + forward * tangentOffset.y;

            var upHint = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (upHint.sqrMagnitude < 0.001f)
            {
                upHint = Vector3.ProjectOnPlane(Vector3.right, normal);
            }

            textObject.transform.rotation = Quaternion.LookRotation(-normal, upHint.normalized);
            var text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.11f;
            text.color = color;
            textObject.AddComponent<WorldTextBillboard>();
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

        private static GameObject CreateLocalCube(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool removeCollider = false)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;
            SetMaterial(cube, material);
            if (removeCollider)
            {
                RemoveCollider(cube);
            }

            return cube;
        }

        private static ShipStation CreateShipStation(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, ShipStationRole role, string displayName)
        {
            var stationObject = CreateLocalCube(name, parent, localPosition, localScale, material);
            stationObject.AddComponent<NetworkObject>();
            var station = stationObject.AddComponent<ShipStation>();
            var stationSo = new SerializedObject(station);
            stationSo.FindProperty("displayName").stringValue = displayName;
            stationSo.FindProperty("role").enumValueIndex = (int)role;
            stationSo.ApplyModifiedPropertiesWithoutUndo();
            return station;
        }

        private static void CreateLocalText(string name, Transform parent, string content, Vector3 localPosition, Quaternion localRotation, Color color)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = localPosition;
            textObject.transform.localRotation = localRotation;
            var text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.12f;
            text.color = color;
            textObject.AddComponent<WorldTextBillboard>();
        }

        private static void EnsureBootstrapperMonsterReferences(RoamingMonster monsterPrefab)
        {
            var bootstrapper = Object.FindFirstObjectByType<PrototypeNetworkBootstrapper>();
            if (bootstrapper == null || monsterPrefab == null)
            {
                return;
            }

            var spawnPoints = EnsureMonsterSpawnPointsInOpenScene();
            var serializedBootstrapper = new SerializedObject(bootstrapper);
            serializedBootstrapper.FindProperty("monsterPrefab").objectReferenceValue = monsterPrefab;
            AssignObjectArray(serializedBootstrapper.FindProperty("monsterSpawnPoints"), spawnPoints);
            serializedBootstrapper.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrapper);
        }

        private static Transform[] EnsureMonsterSpawnPointsInOpenScene()
        {
            var world = Object.FindFirstObjectByType<SphereWorld>();
            if (world == null)
            {
                return System.Array.Empty<Transform>();
            }

            var root = GameObject.Find("Enemy Spawn Points");
            if (root == null)
            {
                root = new GameObject("Enemy Spawn Points");
            }

            var spawn = GameObject.Find("Monster Spawn 1");
            if (spawn == null)
            {
                spawn = new GameObject("Monster Spawn 1");
                spawn.transform.SetParent(root.transform, true);
            }

            PlaceOnSurface(world, spawn, new Vector3(0.08f, -1f, 0.04f), 0.7f, Vector3.forward);
            EditorUtility.SetDirty(spawn);
            EditorUtility.SetDirty(root);
            return new[] { spawn.transform };
        }

        private static void EnsureBootstrapperLootReferences(NetworkLootItem[] lootPrefabs)
        {
            var bootstrapper = Object.FindFirstObjectByType<PrototypeNetworkBootstrapper>();
            if (bootstrapper == null)
            {
                return;
            }

            var spawnPoints = EnsureLootSpawnPointsInOpenScene();
            var serializedBootstrapper = new SerializedObject(bootstrapper);
            AssignObjectArray(serializedBootstrapper.FindProperty("lootPrefabs"), lootPrefabs);
            AssignObjectArray(serializedBootstrapper.FindProperty("lootSpawnPoints"), spawnPoints);
            serializedBootstrapper.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrapper);
        }

        private static Transform[] EnsureLootSpawnPointsInOpenScene()
        {
            var world = Object.FindFirstObjectByType<SphereWorld>();
            if (world == null)
            {
                return System.Array.Empty<Transform>();
            }

            var existingRoot = GameObject.Find("Loot Spawn Points");
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            var spawnPoints = CreateLootSpawnPoints();
            foreach (var spawnPoint in spawnPoints)
            {
                if (spawnPoint != null)
                {
                    EditorUtility.SetDirty(spawnPoint);
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return spawnPoints;
        }

        private static void EnsureLaunchpadLayoutInOpenScene(IReadOnlyDictionary<string, Material> materials)
        {
            var existingRoot = GameObject.Find("Launchpad Assembly Site");
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            DestroyOpenSceneObjectsNamed("Launchpad Sign");

            CreateLaunchpad(materials);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static Transform[] EnsureShipInteriorInOpenScene(IReadOnlyDictionary<string, Material> materials)
        {
            var existingRoot = GameObject.Find(ShipInteriorRootName);
            if (existingRoot != null)
            {
                Object.DestroyImmediate(existingRoot);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            var spawns = CreateShipInterior(materials);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return spawns;
        }

        private static void EnsureSceneTransitionServiceInOpenScene()
        {
            if (Object.FindFirstObjectByType<NetworkSceneTransitionService>() != null)
            {
                return;
            }

            var networkManager = Object.FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                return;
            }

            networkManager.gameObject.AddComponent<NetworkSceneTransitionService>();
            EditorUtility.SetDirty(networkManager.gameObject);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        private static void EnsureBootstrapperShipReferences(Transform[] shipSpawns)
        {
            var bootstrapper = Object.FindFirstObjectByType<PrototypeNetworkBootstrapper>();
            if (bootstrapper == null)
            {
                return;
            }

            var serializedBootstrapper = new SerializedObject(bootstrapper);
            AssignObjectArray(serializedBootstrapper.FindProperty("shipSpawnPoints"), shipSpawns);
            serializedBootstrapper.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrapper);
        }

        private static void DestroyOpenSceneObjectsNamed(string objectName)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null ||
                    gameObject.name != objectName ||
                    gameObject.scene != scene ||
                    EditorUtility.IsPersistent(gameObject))
                {
                    continue;
                }

                Object.DestroyImmediate(gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private static void EnsureOpenSceneGameplayTuning()
        {
            var world = Object.FindFirstObjectByType<SphereWorld>();
            if (world != null)
            {
                var worldSo = new SerializedObject(world);
                worldSo.FindProperty("radius").floatValue = PlanetRadius;
                worldSo.FindProperty("gravityAcceleration").floatValue = PlanetGravityAcceleration;
                worldSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(world);

                // Keep visual scale in sync with physics radius.
                var planet = world.gameObject;
                planet.transform.localScale = Vector3.one * PlanetRadius * 2f;

                if (planet.GetComponent<NetworkObject>() == null)
                    planet.AddComponent<NetworkObject>();
                if (planet.GetComponent<PlanetColorRandomizer>() == null)
                    planet.AddComponent<PlanetColorRandomizer>();
                if (planet.GetComponent<PlanetTreeSpawner>() == null)
                    planet.AddComponent<PlanetTreeSpawner>();

                EditorUtility.SetDirty(planet);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            // Ensure exactly one Day Night Manager exists.
            var allDayNightManagers = Object.FindObjectsByType<DayNightCycle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 1; i < allDayNightManagers.Length; i++)
            {
                Object.DestroyImmediate(allDayNightManagers[i].gameObject);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            if (allDayNightManagers.Length == 0)
            {
                var dayNightManager = new GameObject("Day Night Manager");
                dayNightManager.AddComponent<NetworkObject>();
                dayNightManager.AddComponent<DayNightCycle>();
                EditorUtility.SetDirty(dayNightManager);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            RemoveLaunchpadCollisionInOpenScene();
        }

        private static void RemoveLaunchpadCollisionInOpenScene()
        {
            var partLaunchpad = GameObject.Find("Part Launchpad");
            if (partLaunchpad != null)
            {
                foreach (var collider in partLaunchpad.GetComponents<Collider>())
                {
                    if (collider != null)
                    {
                        Object.DestroyImmediate(collider);
                    }
                }

                EditorUtility.SetDirty(partLaunchpad);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            var launchpadRoot = GameObject.Find("Launchpad Assembly Site");
            if (launchpadRoot != null)
            {
                var colliders = launchpadRoot.GetComponentsInChildren<Collider>(true);
                foreach (var collider in colliders)
                {
                    if (collider == null)
                    {
                        continue;
                    }

                    if (collider.GetComponent<LaunchpadZone>() != null)
                    {
                        Object.DestroyImmediate(collider);
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        continue;
                    }

                    if (!collider.isTrigger)
                    {
                        Object.DestroyImmediate(collider);
                    }
                }
            }

            var decorativeNames = new[]
            {
                "Crash Dirt Patch",
                "Launchpad Cable A",
                "Launchpad Cable B"
            };

            foreach (var objectName in decorativeNames)
            {
                var target = GameObject.Find(objectName);
                if (target == null)
                {
                    continue;
                }

                var colliders = target.GetComponentsInChildren<Collider>(true);
                foreach (var collider in colliders)
                {
                    if (collider == null || collider.isTrigger)
                    {
                        continue;
                    }

                    Object.DestroyImmediate(collider);
                }
            }
        }

        private static NetworkLootItem[] LoadLootPrefabs()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { LootPrefabFolderPath });
            var prefabs = new List<NetworkLootItem>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var lootPrefab = AssetDatabase.LoadAssetAtPath<NetworkLootItem>(path);
                if (lootPrefab != null)
                {
                    prefabs.Add(lootPrefab);
                }
            }

            return prefabs.ToArray();
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

            var changed = false;
            if (material.color != color)
            {
                material.color = color;
                changed = true;
            }

            if (name == "GlowCube")
            {
                if (!material.IsKeywordEnabled("_EMISSION"))
                {
                    material.EnableKeyword("_EMISSION");
                    changed = true;
                }

                var emissionColor = color * 1.4f;
                if (material.HasProperty("_EmissionColor") && material.GetColor("_EmissionColor") != emissionColor)
                {
                    material.SetColor("_EmissionColor", emissionColor);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

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

        private static void DisableCollider(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private static void RemoveCollider(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
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
                new LootSpec("Cockpit Nosecone", 0, 0.72f, PrimitiveType.Capsule, new Vector3(-0.42f, 0.82f, 0.38f), new Vector3(0.95f, 1.25f, 0.95f), "ShipPart", ShipPartType.Cockpit),
                new LootSpec("Bent Rocket Wings", 0, 0.68f, PrimitiveType.Cube, new Vector3(0.48f, 0.78f, 0.42f), new Vector3(2.3f, 0.28f, 0.85f), "ShipPart", ShipPartType.Wings),
                new LootSpec("Coughing Engine", 0, 0.62f, PrimitiveType.Cylinder, new Vector3(0.04f, 0.78f, -0.62f), new Vector3(0.75f, 1.25f, 0.75f), "ShipPart", ShipPartType.Engine),
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

        private static float GetLootSurfaceOffset(LootSpec spec)
        {
            return spec.Shape switch
            {
                PrimitiveType.Cube => spec.Scale.y * 0.5f + 0.04f,
                PrimitiveType.Sphere => Mathf.Max(spec.Scale.x, spec.Scale.y, spec.Scale.z) * 0.5f + 0.04f,
                PrimitiveType.Cylinder => spec.Scale.y + 0.04f,
                PrimitiveType.Capsule => spec.Scale.y + 0.04f,
                _ => 0.5f
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
