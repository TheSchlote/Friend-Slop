using System.Text;
using FriendSlop.Networking;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // Menu layout, string formatters, and menu-action handlers.
    // See FriendSlopUI.cs for fields and the main RefreshUi coordinator.
    public partial class FriendSlopUI
    {
        private void LayoutMenu(bool connected, bool isHost, RoundPhase phase)
        {
            var canvasSize = GetCanvasSize();
            var showJoinCodePanel = joinCodePanelRoot != null && joinCodePanelRoot.activeSelf;
            var showConnectionHint = connectionHintText != null && connectionHintText.gameObject.activeSelf;
            var menuWidth = Mathf.Clamp(canvasSize.x * 0.34f, MinMenuWidth, MaxMenuWidth);
            var menuHeight = connected
                ? showJoinCodePanel
                    ? Mathf.Clamp(canvasSize.y * 0.66f, 680f, 760f)
                    : Mathf.Clamp(canvasSize.y * 0.44f, MinConnectedMenuHeight, MaxConnectedMenuHeight)
                : Mathf.Clamp(canvasSize.y * 0.58f, MinDisconnectedMenuHeight, MaxDisconnectedMenuHeight);
            var contentWidth = menuWidth - 64f;
            var buttonWidth = Mathf.Clamp(menuWidth * 0.64f, MinButtonWidth, MaxButtonWidth);
            var buttonHeight = Mathf.Clamp(canvasSize.y * 0.045f, MinButtonHeight, MaxButtonHeight);
            var buttonGap = Mathf.Clamp(canvasSize.y * 0.014f, 12f, 18f);

            menuRect.sizeDelta = new Vector2(menuWidth, menuHeight);
            SetSize(titleText.rectTransform, new Vector2(contentWidth, 38f));
            SetSize(statusText.rectTransform, new Vector2(contentWidth, showJoinCodePanel ? 64f : 88f));
            if (connectionHintText != null)
                SetSize(connectionHintText.rectTransform, new Vector2(contentWidth, 64f));
            SetSize(lobbyQueueText.rectTransform, new Vector2(contentWidth, showJoinCodePanel ? 64f : 96f));
            if (joinCodePanelRect != null)
            {
                SetSize(joinCodePanelRect, new Vector2(buttonWidth, 126f));
                SetSize(joinCodeLabelText.rectTransform, new Vector2(buttonWidth - 20f, 18f));
                SetSize(joinCodeText.rectTransform, new Vector2(buttonWidth - 24f, 34f));
                SetButtonSize(copyCodeButton, Mathf.Min(buttonWidth - 24f, 230f), 30f);
            }
            SetButtonSize(hostButton, buttonWidth, buttonHeight);
            SetButtonSize(joinButton, buttonWidth, buttonHeight);
            SetButtonSize(localHostButton, buttonWidth, buttonHeight);
            SetButtonSize(localJoinButton, buttonWidth, buttonHeight);
            SetButtonSize(cancelButton, buttonWidth, buttonHeight);
            SetButtonSize(startButton, buttonWidth, buttonHeight);
            SetButtonSize(restartButton, buttonWidth, buttonHeight);
            SetButtonSize(cyclePlanetButton, buttonWidth, buttonHeight);
            SetButtonSize(shutdownButton, buttonWidth, buttonHeight);
            SetButtonSize(quitButton, buttonWidth, buttonHeight);
            SetSize(joinInput.GetComponent<RectTransform>(), new Vector2(buttonWidth, buttonHeight));
            SetSize(gameOverText.rectTransform, new Vector2(Mathf.Max(contentWidth, 520f), 90f));

            var menuTop = menuHeight * 0.5f;
            var gameOverY = menuTop + 78f;
            var namePanelY = phase == RoundPhase.AllDead ? gameOverY + 104f : menuTop + 72f;
            SetPosition(gameOverText.rectTransform, new Vector2(0f, gameOverY));
            SetPosition(namePanelRect, new Vector2(0f, namePanelY));
            SetPosition(statusText.rectTransform, new Vector2(0f, showJoinCodePanel ? -70f : -88f));
            if (connectionHintText != null)
                SetPosition(connectionHintText.rectTransform, new Vector2(0f, showConnectionHint ? 52f : -176f));
            SetPosition(lobbyQueueText.rectTransform, new Vector2(0f, showJoinCodePanel ? -302f : -176f));
            if (joinCodePanelRect != null)
            {
                SetPosition(joinCodePanelRect, new Vector2(0f, 112f));
                SetPosition(joinCodeLabelText.rectTransform, new Vector2(0f, 50f));
                SetPosition(joinCodeText.rectTransform, new Vector2(0f, 18f));
                SetPosition(copyCodeButton.GetComponent<RectTransform>(), new Vector2(0f, -42f));
            }

            if (!connected)
            {
                if (cancelButton != null && cancelButton.gameObject.activeSelf)
                {
                    SetPosition(cancelButton.GetComponent<RectTransform>(), new Vector2(0f, -32f));
                    SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, -menuHeight * 0.5f + buttonHeight * 0.7f));
                    return;
                }

                var inputY = 84f;
                var firstButtonY = inputY - buttonHeight - buttonGap;
                SetPosition(joinInput.GetComponent<RectTransform>(), new Vector2(0f, inputY));
                SetPosition(hostButton.GetComponent<RectTransform>(), new Vector2(0f, firstButtonY));
                SetPosition(joinButton.GetComponent<RectTransform>(), new Vector2(0f, firstButtonY - (buttonHeight + buttonGap)));
                SetPosition(localHostButton.GetComponent<RectTransform>(), new Vector2(0f, firstButtonY - (buttonHeight + buttonGap) * 2f));
                SetPosition(localJoinButton.GetComponent<RectTransform>(), new Vector2(0f, firstButtonY - (buttonHeight + buttonGap) * 3f));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, -menuHeight * 0.5f + buttonHeight * 0.7f));
                return;
            }

            var primaryButtonY = showJoinCodePanel ? -96f : -48f;
            var secondaryButtonY = primaryButtonY - buttonHeight - buttonGap;
            var tertiaryButtonY = secondaryButtonY - buttonHeight - buttonGap;
            var quaternaryButtonY = tertiaryButtonY - buttonHeight - buttonGap;
            EnsureTestModeBuilt();
            if (isHost && phase == RoundPhase.Lobby)
            {
                SetPosition(startButton.GetComponent<RectTransform>(), new Vector2(0f, primaryButtonY));
                // Test Mode sits directly below Start Round; everything else shifts down a slot.
                RefreshTestMode(connected, isHost, phase, buttonWidth, buttonHeight, buttonGap, secondaryButtonY);
                SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, tertiaryButtonY));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, quaternaryButtonY));
                return;
            }
            RefreshTestMode(connected, isHost, phase, buttonWidth, buttonHeight, buttonGap, secondaryButtonY);

            if (isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed || phase == RoundPhase.AllDead))
            {
                var showCycle = cyclePlanetButton != null && cyclePlanetButton.gameObject.activeSelf;
                SetPosition(restartButton.GetComponent<RectTransform>(), new Vector2(0f, primaryButtonY));
                if (showCycle)
                {
                    SetPosition(cyclePlanetButton.GetComponent<RectTransform>(), new Vector2(0f, secondaryButtonY));
                    SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, tertiaryButtonY));
                    SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, tertiaryButtonY - buttonHeight - buttonGap));
                }
                else
                {
                    SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, secondaryButtonY));
                    SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, tertiaryButtonY));
                }
                return;
            }

            SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, primaryButtonY));
            SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, secondaryButtonY));
        }

        // ── Menu action handlers ──────────────────────────────────────────────

        private void OnRestartOrTravelClicked()
        {
            var rm = RoundManagerRegistry.Current;
            if (rm == null) return;

            if (rm.Phase.Value == RoundPhase.Success && rm.GetOfferedNextPlanetChoices().Count > 0 && !rm.HasReachedFinalTier)
                rm.RequestTravelToNextPlanetServerRpc();
            else
                rm.RequestRestartRoundServerRpc();

            LockGameplayCursor();
        }

        private void OnCyclePlanetClicked()
        {
            var rm = RoundManagerRegistry.Current;
            if (rm == null || rm.Catalog == null) return;
            var choices = rm.GetOfferedNextPlanetChoices();
            if (choices.Count <= 1) return;

            var current = rm.SelectedNextPlanet;
            var idx = current != null ? choices.IndexOf(current) : -1;
            var nextPlanet = choices[(idx + 1) % choices.Count];
            var catalogIndex = rm.Catalog.IndexOf(nextPlanet);
            if (catalogIndex >= 0)
                rm.RequestSelectNextPlanetServerRpc(catalogIndex);
        }

        // ── Dynamic button label helpers ──────────────────────────────────────

        private void UpdateRestartButtonLabel(RoundManager round, RoundPhase phase)
        {
            if (restartButtonLabel == null) return;
            if (round != null && phase == RoundPhase.Success && !round.HasReachedFinalTier)
            {
                var choices = round.GetOfferedNextPlanetChoices();
                if (choices.Count > 0)
                {
                    var next = round.SelectedNextPlanet;
                    if (next == null || next.Tier != round.NextTier || !choices.Contains(next))
                        next = choices[0];
                    restartButtonLabel.text = $"Travel: {FormatPlanetLabel(next)}";
                    return;
                }
            }
            restartButtonLabel.text = "Restart Round";
        }

        private void UpdateCyclePlanetButton(RoundManager round, RoundPhase phase, bool connected, bool isHost)
        {
            if (cyclePlanetButton == null) return;
            var show = connected && isHost && round != null && phase == RoundPhase.Success
                       && !round.HasReachedFinalTier && round.GetOfferedNextPlanetChoices().Count > 1;
            cyclePlanetButton.gameObject.SetActive(show);
            if (show && cyclePlanetButtonLabel != null)
                cyclePlanetButtonLabel.text = $"Cycle Tier {round.NextTier} Planet";
        }

        // ── Join-code panel ───────────────────────────────────────────────────

        private void UpdateJoinCodePanel(NetworkSessionManager session, bool showMenu, bool connected, bool isHost)
        {
            if (joinCodePanelRoot == null) return;

            var rawCode = session != null ? session.LastJoinCode : string.Empty;
            var copyableCode = GetCopyableJoinCode(rawCode);
            var showJoinCode = showMenu && connected && isHost && !string.IsNullOrWhiteSpace(copyableCode);
            joinCodePanelRoot.SetActive(showJoinCode);
            if (!showJoinCode) return;

            joinCodeLabelText.text = IsLanJoinCode(rawCode) ? "LAN ADDRESS - COPY/PASTE" : "JOIN CODE - COPY/PASTE";
            joinCodeText.text = FormatReadableJoinCode(copyableCode);
            var copied = Time.unscaledTime < _copyCodeFeedbackUntil;
            if (copyCodeButtonText != null)
                copyCodeButtonText.text = copied ? "Copied to Clipboard" : "Copy Code";

            SetButtonColor(copyCodeButton, copied ? SuccessButtonColor : DefaultButtonColor);
        }

        private void CopyJoinCodeToClipboard()
        {
            var session = SessionManager;
            var code = GetCopyableJoinCode(session != null ? session.LastJoinCode : string.Empty);
            if (string.IsNullOrWhiteSpace(code)) return;

            GUIUtility.systemCopyBuffer = code;
            _copyCodeFeedbackUntil = Time.unscaledTime + 1.5f;
            if (copyCodeButtonText != null)
                copyCodeButtonText.text = "Copied to Clipboard";

            SetButtonColor(copyCodeButton, SuccessButtonColor);
        }

        private static string GetCopyableJoinCode(string rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode)) return string.Empty;
            var trimmed = rawCode.Trim();
            return IsLanJoinCode(trimmed) ? trimmed.Substring("LAN:".Length).Trim() : trimmed;
        }

        private static bool IsLanJoinCode(string rawCode)
        {
            return !string.IsNullOrWhiteSpace(rawCode)
                && rawCode.Trim().StartsWith("LAN:", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatReadableJoinCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || JoinCodeUtility.LooksLikeLanAddress(code))
                return code;

            code = JoinCodeUtility.NormalizeJoinCode(code);
            var builder = new StringBuilder(code.Length + code.Length / 3);
            for (var i = 0; i < code.Length; i++)
            {
                if (i > 0 && i % 3 == 0) builder.Append(' ');
                builder.Append(code[i]);
            }
            return builder.ToString();
        }

        // ── Status / HUD text builders ────────────────────────────────────────

        private static string BuildStatusText(NetworkSessionManager session, RoundManager round, bool connectionInProgress)
        {
            var status = session != null ? session.Status : "Not connected.";
            if (connectionInProgress)
                status = $"{status} {GetActivityDots()}";

            return status + $"\nTeam Money: ${GetTeamMoney(round)}\nTab/Esc toggles menu.";
        }

        private static string BuildConnectionHint(NetworkSessionManager session)
        {
            var remaining = session != null ? session.PendingConnectionSecondsRemaining : 0f;
            if (remaining > 0f)
                return $"Still trying for {Mathf.CeilToInt(remaining)}s.\nCancel returns to this menu safely.";

            return "Contacting Unity online services.\nCancel returns to this menu safely.";
        }

        private static string GetActivityDots()
        {
            var count = 1 + Mathf.FloorToInt(Time.unscaledTime * 2f) % 3;
            return new string('.', count).PadRight(3);
        }

        private static string FormatPart(bool installed, string label)
        {
            return installed ? $"{label} OK" : $"{label} missing";
        }

        private static string BuildObjectiveHudText(RoundManager round)
        {
            var objective = round != null ? round.ActiveObjective : null;
            if (objective != null)
            {
                var hud = objective.BuildHudStatus(round);
                if (!string.IsNullOrEmpty(hud)) return hud;
                if (!string.IsNullOrEmpty(objective.Title)) return objective.Title;
            }

            // Legacy default — no objective configured, fall back to parts list.
            return round != null
                ? $"Parts: {FormatPart(round.HasCockpit.Value, "Cockpit")} | {FormatPart(round.HasWings.Value, "Wings")} | {FormatPart(round.HasEngine.Value, "Engine")}"
                : "Parts: Cockpit missing | Wings missing | Engine missing";
        }

        private static int GetTeamMoney(RoundManager round)
        {
            return round != null ? round.CollectedValue.Value : 0;
        }

        private static string FormatPlanetLabel(PlanetDefinition planet)
        {
            return planet != null ? $"{planet.DisplayName} (Tier {planet.Tier})" : "Unknown";
        }

        private static string BuildSuccessResultText(RoundManager round)
        {
            if (round == null) return "OBJECTIVE COMPLETE.";

            var objectiveText = round.ActiveObjective != null
                ? round.ActiveObjective.BuildSuccessText(round)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(objectiveText))
                objectiveText = $"OBJECTIVE COMPLETE on {FormatPlanetLabel(round.CurrentPlanet)}.";

            objectiveText = objectiveText.TrimEnd();
            if (round.HasReachedFinalTier)
                return $"{objectiveText}\nFinal tier reached - replay to keep grinding.";

            var choices = round.GetOfferedNextPlanetChoices();
            if (choices.Count == 0)
                return $"{objectiveText}\nNo tier {round.NextTier} planets registered yet - host can replay.";

            var next = round.SelectedNextPlanet;
            if (next == null || next.Tier != round.NextTier || !choices.Contains(next))
                next = choices[0];

            var totalForTier = round.Catalog != null ? round.Catalog.GetPlanetsForTier(round.NextTier).Count : choices.Count;
            string optionsLine;
            if (choices.Count <= 1)
                optionsLine = string.Empty;
            else if (totalForTier > choices.Count)
                optionsLine = $"\n{choices.Count} of {totalForTier} tier {round.NextTier} planets rolled - host can cycle between them.";
            else
                optionsLine = $"\n{choices.Count} tier {round.NextTier} options - host can cycle.";
            return $"{objectiveText}\nNext: {FormatPlanetLabel(next)}{optionsLine}";
        }

        private static string BuildFailureResultText(RoundManager round)
        {
            if (round == null) return "FAILED: back aboard the ship.\nHost can restart the planet run.";

            var objectiveText = round.ActiveObjective != null
                ? round.ActiveObjective.BuildFailureText(round)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(objectiveText))
                objectiveText = "FAILED: back aboard the ship.";

            return $"{objectiveText.TrimEnd()}\nHost can restart the planet run.";
        }

        private static string BuildLobbyQueue(NetworkManager networkManager)
        {
            if (networkManager == null || !networkManager.IsListening) return string.Empty;

            var builder = new StringBuilder();
            builder.Append("Lobby Queue");

            var ids = networkManager.ConnectedClientsIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var clientId = ids[i];
                builder.Append('\n');
                builder.Append(i + 1);
                builder.Append(". ");
                builder.Append(clientId == NetworkManager.ServerClientId ? "Host" : $"Player {clientId}");
                if (clientId == networkManager.LocalClientId)
                    builder.Append(" (you)");
            }

            return builder.ToString();
        }
    }
}
