using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // Small HUD compass that points toward the active launchpad.
    public partial class FriendSlopUI
    {
        private RectTransform compassPanelRect;
        private Text compassArrowText;

        private void BuildCompass(GameObject canvasObject)
        {
            const float size = 80f;
            var panel = CreatePanel("CompassPanel", canvasObject.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-14f, 14f), new Vector2(size, size),
                new Color(0.02f, 0.03f, 0.03f, 0.72f));
            compassPanelRect = panel.GetComponent<RectTransform>();

            var label = CreateText("CompassLabel", panel.transform, "LAUNCHPAD", 10,
                TextAnchor.LowerCenter,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 4f), new Vector2(0f, 14f));
            label.color = new Color(0.95f, 0.78f, 0.18f, 0.85f);

            compassArrowText = CreateText("CompassArrow", panel.transform, "^", 28,
                TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 6f), new Vector2(40f, 40f));
            var arrowOutline = compassArrowText.gameObject.AddComponent<Outline>();
            arrowOutline.effectColor = Color.black;
            arrowOutline.effectDistance = new Vector2(1f, -1f);

            compassPanelRect.gameObject.SetActive(false);
        }

        private void UpdateCompass(NetworkFirstPersonController localPlayer, RoundPhase phase)
        {
            if (compassPanelRect == null) return;

            var show = localPlayer != null
                && localPlayer.PlayerCamera != null
                && phase == RoundPhase.Active;

            if (compassPanelRect.gameObject.activeSelf != show)
                compassPanelRect.gameObject.SetActive(show);

            if (!show || compassArrowText == null) return;

            Vector3? launchpadPos = null;
            for (var i = 0; i < PlanetEnvironment.ActiveEnvironments.Count; i++)
            {
                var env = PlanetEnvironment.ActiveEnvironments[i];
                if (env != null && env.LaunchpadZone != null)
                {
                    launchpadPos = env.LaunchpadZone.transform.position;
                    break;
                }
            }

            if (launchpadPos == null)
            {
                compassArrowText.text = "?";
                compassArrowText.rectTransform.localEulerAngles = Vector3.zero;
                return;
            }

            compassArrowText.text = "^";
            var playerPos = localPlayer.transform.position;
            var up = FlatGravityVolume.GetGravityUp(playerPos);
            var toTargetFlat = Vector3.ProjectOnPlane(launchpadPos.Value - playerPos, up);
            var forwardFlat = Vector3.ProjectOnPlane(localPlayer.PlayerCamera.transform.forward, up);

            var angle = toTargetFlat.sqrMagnitude > 0.001f && forwardFlat.sqrMagnitude > 0.001f
                ? Vector3.SignedAngle(forwardFlat, toTargetFlat, up)
                : 0f;

            compassArrowText.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle);
        }
    }
}
