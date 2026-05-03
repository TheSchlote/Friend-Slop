using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // HUD bars, overlays, and per-frame visual effects.
    // See FriendSlopUI.cs for fields and the main RefreshUi coordinator.
    public partial class FriendSlopUI
    {
        private void LayoutHud()
        {
            var canvasSize = GetCanvasSize();
            var hudWidth = Mathf.Clamp(canvasSize.x * 0.48f, 680f, 920f);
            var moneyWidth = Mathf.Clamp(canvasSize.x * 0.22f, 300f, 440f);

            SetSize(hudRect, new Vector2(hudWidth, 180f));
            SetSize(timerText.rectTransform, new Vector2(hudWidth, 32f));
            SetSize(carriedText.rectTransform, new Vector2(Mathf.Min(hudWidth, 700f), 30f));
            SetSize(resultText.rectTransform, new Vector2(Mathf.Min(hudWidth, 820f), 42f));
            SetSize(moneyPanelRect, new Vector2(moneyWidth, 44f));
        }

        private void UpdateInventoryHud(NetworkFirstPersonController localPlayer, bool activeRound)
        {
            if (inventoryPanelRect == null) return;

            var show = activeRound && localPlayer != null && !localPlayer.IsDeadLocally;
            if (inventoryPanelRect.gameObject.activeSelf != show)
                inventoryPanelRect.gameObject.SetActive(show);
            if (!show)
            {
                // Clear the preview rig so dead/menu states don't leave ghost meshes spinning.
                if (inventoryPreviewRig != null)
                {
                    for (var i = 0; i < NetworkFirstPersonController.InventorySize; i++)
                        inventoryPreviewRig.SetSlotItem(i, null);
                }
                return;
            }

            var activeSlot = Mathf.Clamp(localPlayer.ActiveInventorySlot.Value, 0, NetworkFirstPersonController.InventorySize - 1);
            for (var i = 0; i < NetworkFirstPersonController.InventorySize; i++)
            {
                var item = localPlayer.GetInventoryItem(i);
                var isActive = i == activeSlot;
                if (inventorySlotBackgrounds[i] != null)
                {
                    inventorySlotBackgrounds[i].color = isActive
                        ? InventorySlotActiveColor
                        : item != null ? InventorySlotFilledColor : InventorySlotEmptyColor;
                }
                if (inventorySlotBorders[i] != null)
                {
                    inventorySlotBorders[i].color = isActive
                        ? InventorySlotBorderActiveColor
                        : InventorySlotBorderIdleColor;
                }
                if (inventorySlotPreviews[i] != null)
                    inventorySlotPreviews[i].enabled = item != null;
                if (inventorySlotItemTexts[i] != null)
                {
                    inventorySlotItemTexts[i].text = item != null ? TruncateForSlot(item.ItemName) : "Empty";
                    inventorySlotItemTexts[i].color = item != null
                        ? Color.white
                        : new Color(1f, 1f, 1f, 0.4f);
                }
                if (inventorySlotValueTexts[i] != null)
                {
                    inventorySlotValueTexts[i].text = item != null && !item.IsShipPart
                        ? $"${item.Value}"
                        : item != null ? item.ShipPartType.ToString().ToUpperInvariant()
                        : string.Empty;
                }
                if (inventoryPreviewRig != null)
                    inventoryPreviewRig.SetSlotItem(i, item);
            }
        }

        private static string TruncateForSlot(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            const int maxLength = 14;
            return text.Length <= maxLength ? text : text[..maxLength];
        }

        public void ShowDamageFlash()
        {
            _damageFlashAlpha = 0.44f;
            if (damageFlashImage != null)
                damageFlashImage.color = new Color(0.72f, 0f, 0f, _damageFlashAlpha);
        }

        public void ShowLateJoinLoading()
        {
            _lateJoinLoading = true;
            _lateJoinLoadingStartTime = Time.time;
            activeMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static bool IsActiveRound()
        {
            var networkManager = NetworkManager.Singleton;
            var round = RoundManagerRegistry.Current;
            return networkManager != null &&
                   networkManager.IsListening &&
                   round != null &&
                   round.Phase.Value == RoundPhase.Active;
        }

        private void UpdateLoadingScreen(bool isLoading, RoundPhase phase, RoundManager round)
        {
            if (loadingScreenRoot == null) return;
            loadingScreenRoot.SetActive(isLoading);
            if (!isLoading)
            {
                _loadingProgressActive = false;
                _loadingProgressValue = 0f;
                return;
            }

            if (!_loadingProgressActive || _loadingProgressPhase != phase)
            {
                _loadingProgressActive = true;
                _loadingProgressPhase = phase;
                _loadingProgressStartTime = Time.unscaledTime;
                _loadingProgressValue = 0f;
            }

            float targetProgress;
            string statusMsg;
            if (phase == RoundPhase.Transitioning && round != null)
            {
                var dest = round.SelectedNextPlanet ?? round.CurrentPlanet;
                var destName = dest != null ? dest.DisplayName : "Unknown";
                statusMsg = $"Traveling to {destName}...";
                targetProgress = Mathf.Clamp01((Time.unscaledTime - _loadingProgressStartTime) /
                                                TransitionProgressFillSeconds) * TransitionProgressMax;
            }
            else if (phase == RoundPhase.Loading && round != null)
            {
                var ready = round.PlayersReady.Value;
                var expected = Mathf.Max(1, round.PlayersExpectedToLoad.Value);
                targetProgress = Mathf.Clamp01((float)ready / expected);
                statusMsg = ready >= expected
                    ? "All players ready!"
                    : $"Waiting for players... ({ready} / {expected})";
            }
            else
            {
                targetProgress = Mathf.Clamp01((Time.unscaledTime - _lateJoinLoadingStartTime) / LateJoinLoadingDuration);
                statusMsg = "Syncing world...";
            }

            var progress = Mathf.Max(_loadingProgressValue, targetProgress);
            if (phase == RoundPhase.Transitioning)
                progress = Mathf.Min(progress, TransitionProgressMax);
            _loadingProgressValue = progress;

            if (loadingStatusText != null)
                loadingStatusText.text = statusMsg;

            if (loadingBarFillRect != null)
            {
                var barBg = loadingBarFillRect.parent as RectTransform;
                var barWidth = barBg != null ? Mathf.Max(0f, barBg.rect.width - 4f) : 496f;
                loadingBarFillRect.sizeDelta = new Vector2(barWidth * progress, 16f);
            }
        }

        private void UpdateSunGlare()
        {
            if (_sunGlareImage == null) return;

            // Hide glare during loading or when no camera is active.
            var isLoading = _lateJoinLoading;
            var round = RoundManagerRegistry.Current;
            if (round != null && (round.Phase.Value == RoundPhase.Loading || round.Phase.Value == RoundPhase.Transitioning))
                isLoading = true;
            if (isLoading)
            {
                _sunGlareImage.color = new Color(1f, 0.95f, 0.82f, 0f);
                return;
            }

            var localPlayer = LocalPlayerRegistry.Current;
            var cam = localPlayer?.PlayerCamera;
            if (cam == null || localPlayer.IsDeadLocally)
            {
                _sunGlareImage.color = new Color(1f, 0.95f, 0.82f, 0f);
                return;
            }

            if (_dayNightCycle == null)
                _dayNightCycle = Object.FindFirstObjectByType<DayNightCycle>();
            if (_dayNightCycle == null)
            {
                _sunGlareImage.color = new Color(1f, 0.95f, 0.82f, 0f);
                return;
            }

            var rawToSun  = _dayNightCycle.SunWorldPosition - cam.transform.position;
            var distToSun = rawToSun.magnitude;
            var toSun     = rawToSun / distToSun;
            var lookDot   = Vector3.Dot(cam.transform.forward, toSun);
            // Glare begins at about 18 degrees off-center and peaks near dead center.
            var glareT = Mathf.Clamp01((lookDot - 0.95f) / 0.048f);

            if (glareT > 0f)
            {
                // Use local-horizon elevation so the planet naturally blocks glare on the night side.
                var localSunElevation = Vector3.Dot(cam.transform.position.normalized, toSun);
                glareT *= Mathf.Clamp01(localSunElevation * 4f);
            }

            if (glareT > 0f)
            {
                // Block glare when a nearby object (tree, rock, building) is in the line of sight.
                // Start the ray slightly in front of the camera to clear the player's own geometry.
                var rayOrigin = cam.transform.position + cam.transform.forward * 0.5f;
                if (Physics.Raycast(rayOrigin, toSun, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    glareT = 0f;
            }

            var alpha = glareT * glareT * 0.88f;
            _sunGlareImage.color = new Color(1f, 0.95f, 0.82f, alpha);
        }

        private void UpdateFade()
        {
            if (_fadeOverlayImage == null) return;
            var round = RoundManagerRegistry.Current;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var targetAlpha = phase == RoundPhase.Transitioning ? 1f : 0f;
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, targetAlpha, FadeSpeed * Time.deltaTime);
            var combinedAlpha = Mathf.Max(_fadeAlpha, ComputeTeleporterFlashAlpha());
            _fadeOverlayImage.color = new Color(0f, 0f, 0f, combinedAlpha);
        }

        private void RequestTeleporterFlash()
        {
            _teleporterFlashStartTime = Time.time;
        }

        private float ComputeTeleporterFlashAlpha()
        {
            if (_teleporterFlashStartTime < 0f) return 0f;
            var elapsed = Time.time - _teleporterFlashStartTime;
            if (elapsed <= TeleporterFlashAttackSeconds)
                return Mathf.Clamp01(elapsed / TeleporterFlashAttackSeconds);

            var holdEnd = TeleporterFlashAttackSeconds + TeleporterFlashHoldSeconds;
            if (elapsed <= holdEnd) return 1f;

            var releaseProgress = (elapsed - holdEnd) / TeleporterFlashReleaseSeconds;
            if (releaseProgress < 1f)
                return 1f - releaseProgress;

            _teleporterFlashStartTime = -1f;
            return 0f;
        }

        private void UpdateStaminaBar(NetworkFirstPersonController localPlayer, bool activeRound)
        {
            if (staminaPanelRect == null) return;

            var show = activeRound && localPlayer != null && !localPlayer.IsDeadLocally;
            if (staminaPanelRect.gameObject.activeSelf != show)
                staminaPanelRect.gameObject.SetActive(show);
            if (!show) return;

            var percent = Mathf.Clamp01(localPlayer.StaminaPercent);
            var innerWidth = Mathf.Max(0f, staminaPanelRect.rect.width - 4f);
            staminaFillRect.sizeDelta = new Vector2(innerWidth * percent, 18f);

            if (percent > 0.6f)
                staminaFillImage.color = new Color(0.25f, 0.82f, 0.35f, 0.92f);   // green
            else if (percent > 0.25f)
                staminaFillImage.color = new Color(0.92f, 0.78f, 0.18f, 0.92f);   // yellow
            else
                staminaFillImage.color = new Color(0.92f, 0.24f, 0.18f, 0.92f);   // red
        }

        private void UpdateHealthBar(NetworkFirstPersonController localPlayer, bool activeRound)
        {
            if (healthPanelRect == null) return;

            var show = activeRound && localPlayer != null && !localPlayer.IsDeadLocally;
            if (healthPanelRect.gameObject.activeSelf != show)
                healthPanelRect.gameObject.SetActive(show);
            if (!show) return;

            var percent = localPlayer.HealthPercent;
            var innerWidth = Mathf.Max(0f, healthPanelRect.rect.width - 4f);
            healthFillRect.sizeDelta = new Vector2(innerWidth * percent, 18f);

            if (percent > 0.6f)
                healthFillImage.color = new Color(0.25f, 0.82f, 0.35f, 0.92f);
            else if (percent > 0.25f)
                healthFillImage.color = new Color(0.92f, 0.78f, 0.18f, 0.92f);
            else
                healthFillImage.color = new Color(0.92f, 0.24f, 0.18f, 0.92f);

            if (healthLabelText != null)
                healthLabelText.text = $"HP  {localPlayer.CurrentHealth}/{localPlayer.MaxHealth}";
        }

        private void UpdateChargeBar(NetworkFirstPersonController localPlayer, bool activeRound)
        {
            if (chargePanelRect == null) return;

            var interactor = localPlayer != null ? localPlayer.Interactor : null;
            var show = activeRound && interactor != null && interactor.IsCharging && !localPlayer.IsDeadLocally;

            if (chargePanelRect.gameObject.activeSelf != show)
                chargePanelRect.gameObject.SetActive(show);
            if (!show) return;

            var charge = interactor.ChargePercent;
            var innerWidth = Mathf.Max(0f, chargePanelRect.rect.width - 4f);
            chargeFillRect.sizeDelta = new Vector2(innerWidth * charge, 10f);

            chargeFillImage.color = charge > 0.7f
                ? new Color(0.92f, 0.24f, 0.18f, 0.92f)
                : new Color(0.92f, 0.78f, 0.18f, 0.92f);
        }

        private void UpdateDeathOverlay(NetworkFirstPersonController localPlayer, bool activeRound)
        {
            if (deathOverlayText == null) return;

            var isDead = activeRound && localPlayer != null && localPlayer.IsDeadLocally;
            if (!isDead)
            {
                if (deathOverlayText.gameObject.activeSelf) deathOverlayText.gameObject.SetActive(false);
                return;
            }

            if (!deathOverlayText.gameObject.activeSelf) deathOverlayText.gameObject.SetActive(true);

            if (localPlayer.IsSpectatingLocally)
            {
                var label = localPlayer.SpectatorTargetLabel;
                deathOverlayText.text = label == "nobody"
                    ? "YOU DIED\nno survivors to spectate"
                    : $"SPECTATING: {label}\nE / Q to cycle";
            }
            else
            {
                deathOverlayText.text = "YOU DIED";
            }
        }
    }
}
