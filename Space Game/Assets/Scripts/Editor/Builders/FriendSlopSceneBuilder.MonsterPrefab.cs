#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Hazards;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private static RoamingMonster BuildMonsterPrefab(IReadOnlyDictionary<string, Material> materials)
        {
            // Idempotent. See note on BuildPlayerPrefab.
            var existing = AssetDatabase.LoadAssetAtPath<RoamingMonster>(MonsterPrefabPath);
            if (existing != null)
            {
                return existing;
            }

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
    }
}
#endif
