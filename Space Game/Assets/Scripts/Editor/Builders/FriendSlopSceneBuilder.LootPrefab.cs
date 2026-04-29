#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Loot;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private static NetworkLootItem[] BuildLootPrefabs(IReadOnlyDictionary<string, Material> materials)
        {
            var specs = GetLootSpecs();
            var prefabs = new NetworkLootItem[specs.Length];

            // Per-spec idempotency. See note on BuildPlayerPrefab. Renaming a spec without
            // also deleting its old .prefab orphans the old asset; WarnAboutOrphanedLootPrefabs
            // logs those for cleanup.
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var prefabPath = $"{LootPrefabFolderPath}/{SanitizeAssetName(spec.Name)}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<NetworkLootItem>(prefabPath);
                if (existing != null)
                {
                    prefabs[i] = existing;
                    continue;
                }

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

                var prefab = PrefabUtility.SaveAsPrefabAsset(lootObject, prefabPath);
                Object.DestroyImmediate(lootObject);
                prefabs[i] = prefab.GetComponent<NetworkLootItem>();
            }

            WarnAboutOrphanedLootPrefabs(specs);
            return prefabs;
        }

        // Logs a warning for any .prefab in the loot folder whose filename doesn't match
        // a current LootSpec. Detects renamed-without-cleanup specs that would otherwise
        // leave stale prefabs in the NetworkPrefabsList until manually pruned.
        private static void WarnAboutOrphanedLootPrefabs(LootSpec[] specs)
        {
            var expectedNames = new HashSet<string>();
            for (var i = 0; i < specs.Length; i++)
            {
                expectedNames.Add(SanitizeAssetName(specs[i].Name));
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { LootPrefabFolderPath });
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!expectedNames.Contains(fileName))
                {
                    Debug.LogWarning($"Friend Slop: orphaned loot prefab at {path} - no matching LootSpec. Delete it manually if it's no longer used.");
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
