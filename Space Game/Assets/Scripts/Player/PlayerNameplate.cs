using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Player
{
    public class PlayerNameplate : MonoBehaviour
    {
        [SerializeField] private float visibilityDistance = 18f;
        [SerializeField] private float heightOffset = 2.3f;

        private NetworkFirstPersonController controller;
        private GameObject nameplateRoot;
        private TextMesh primaryMesh;
        private TextMesh shadowMesh;

        private void Awake()
        {
            controller = GetComponent<NetworkFirstPersonController>();
            BuildNameplate();
        }

        private void BuildNameplate()
        {
            nameplateRoot = new GameObject("Nameplate");
            nameplateRoot.transform.SetParent(transform, false);
            nameplateRoot.transform.localPosition = Vector3.up * heightOffset;

            // Slight drop-shadow: a second mesh offset behind the first
            var shadowObj = new GameObject("NameplateShadow");
            shadowObj.transform.SetParent(nameplateRoot.transform, false);
            shadowObj.transform.localPosition = new Vector3(0.04f, -0.04f, 0.01f);
            shadowMesh = shadowObj.AddComponent<TextMesh>();
            ConfigureMesh(shadowMesh, new Color(0f, 0f, 0f, 0.6f));

            var primaryObj = new GameObject("NameplateText");
            primaryObj.transform.SetParent(nameplateRoot.transform, false);
            primaryMesh = primaryObj.AddComponent<TextMesh>();
            ConfigureMesh(primaryMesh, Color.white);

            nameplateRoot.SetActive(false);
        }

        private static void ConfigureMesh(TextMesh mesh, Color color)
        {
            mesh.fontSize = 48;
            mesh.characterSize = 0.055f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
        }

        private void LateUpdate()
        {
            var localPlayer = NetworkFirstPersonController.LocalPlayer;

            if (localPlayer == null || controller == localPlayer || nameplateRoot == null)
            {
                if (nameplateRoot != null) nameplateRoot.SetActive(false);
                return;
            }

            var distance = Vector3.Distance(transform.position, localPlayer.transform.position);
            if (distance > visibilityDistance)
            {
                nameplateRoot.SetActive(false);
                return;
            }

            nameplateRoot.SetActive(true);

            // Keep positioned above the player along the local surface normal
            nameplateRoot.transform.position = transform.position + transform.up * heightOffset;

            // Billboard: face the local player's camera
            var cam = localPlayer.PlayerCamera;
            if (cam != null)
            {
                if (WorldTextOrientation.TryGetReadableTextRotation(
                        nameplateRoot.transform.position,
                        cam.transform.position,
                        cam.transform.up,
                        SphereWorld.GetGravityUp(nameplateRoot.transform.position),
                        out var rotation))
                {
                    nameplateRoot.transform.rotation = rotation;
                }
            }

            // Sync text
            var displayName = controller.DisplayName;
            if (primaryMesh.text != displayName)
            {
                primaryMesh.text = displayName;
                shadowMesh.text = displayName;
            }

            // Fade out near the visibility edge
            var fade = 1f - Mathf.Clamp01((distance - visibilityDistance * 0.65f) / (visibilityDistance * 0.35f));
            primaryMesh.color = new Color(1f, 1f, 1f, fade);
            shadowMesh.color = new Color(0f, 0f, 0f, fade * 0.6f);
        }
    }
}
