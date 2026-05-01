#if UNITY_EDITOR
using System.Collections.Generic;
using FriendSlop.Networking;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    public static partial class FriendSlopSceneBuilder
    {
        private static GameObject BuildPlayerPrefab(IReadOnlyDictionary<string, Material> materials)
        {
            // Idempotent: if the prefab already exists, return it untouched. Recreating
            // every call would regenerate every child GameObject's FileID and break any
            // sub-object references (and produce noisy YAML diffs). To deliberately reset
            // the prefab to defaults, delete the .prefab file and re-run Repair. See
            // docs/builder-audit.md.
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (existing != null)
            {
                return existing;
            }

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
    }
}
#endif
