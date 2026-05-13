using FriendSlop.Core;
using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.UI
{
    // Core state, lifecycle, and the main RefreshUi loop.
    // Canvas construction  →  FriendSlopUI.BuildUi.cs
    // HUD bars / overlays  →  FriendSlopUI.Hud.cs
    // Menu layout + text   →  FriendSlopUI.Menu.cs
    public partial class FriendSlopUI : MonoBehaviour
    {
        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            EnsureEventSystem();
            BuildInventoryPreviewRig();
            BuildUi();
        }

        private void OnEnable()
        {
            // Register the input-block predicate so Gameplay code can ask "should I ignore
            // input?" without depending on FriendSlop.UI. See FriendSlop.Core.GameplayInputState.
            GameplayInputState.RegisterBlockProvider(this, IsBlockingGameplayInput);

            NetworkSessionManager.SessionEnded += HandleSessionEnded;
            LocalPlayerRegistry.Damaged += HandleLocalPlayerDamaged;
            LocalPlayerRegistry.JoinedActiveRound += HandleLocalPlayerJoinedActiveRound;
            NetworkFirstPersonController.ChatMessageReceived += OnChatMessageReceived;
            RoundManager.LocalTeleporterFlashRequested += HandleTeleporterFlashRequested;
            RequestUiRefresh();
        }

        private void OnDisable()
        {
            NetworkSessionManager.SessionEnded -= HandleSessionEnded;
            LocalPlayerRegistry.Damaged -= HandleLocalPlayerDamaged;
            LocalPlayerRegistry.JoinedActiveRound -= HandleLocalPlayerJoinedActiveRound;
            NetworkFirstPersonController.ChatMessageReceived -= OnChatMessageReceived;
            RoundManager.LocalTeleporterFlashRequested -= HandleTeleporterFlashRequested;
            ReleaseUiRefreshSubscriptions();
            _chatInputFocused = false;

            GameplayInputState.ClearBlockProvider(this);
        }

        private void HandleSessionEnded() => UnlockMenuCursor();
        private void HandleLocalPlayerDamaged() => ShowDamageFlash();
        private void HandleLocalPlayerJoinedActiveRound() => ShowLateJoinLoading();
        private void HandleTeleporterFlashRequested() => RequestTeleporterFlash();

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            HandleMenuHotkeys();
            var nm = NetworkManager.Singleton;
            HandleChatHotkeys(nm != null && nm.IsListening);
            DetectDisconnection();

            if (_lateJoinLoading && Time.time - _lateJoinLoadingStartTime >= LateJoinLoadingDuration)
            {
                _lateJoinLoading = false;
                if (IsActiveRound())
                    LockGameplayCursor();
                RequestUiRefresh();
            }

            UpdateSunGlare();

            if (_damageFlashAlpha > 0f)
            {
                _damageFlashAlpha = Mathf.Max(0f, _damageFlashAlpha - Time.deltaTime * 1.8f);
                if (damageFlashImage != null)
                    damageFlashImage.color = new Color(0.72f, 0f, 0f, _damageFlashAlpha);
            }

            RefreshUiIfNeeded();
            UpdateFade();
        }

        private void HandleMenuHotkeys()
        {
            if (Keyboard.current == null) return;
            if (_chatInputFocused) return;

            var menuTogglePressed = Keyboard.current.tabKey.wasPressedThisFrame ||
                                    Keyboard.current.escapeKey.wasPressedThisFrame;
            if (!menuTogglePressed) return;

            var networkManager = NetworkManager.Singleton;
            var round = RoundManagerRegistry.Current;
            var connected = networkManager != null && networkManager.IsListening;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var gameplayPhase = connected && RoundStateUtility.AllowsGameplayInput(phase);

            if (!gameplayPhase)
            {
                UnlockMenuCursor();
                return;
            }

            if (activeMenuOpen)
                LockGameplayCursor();
            else
                OpenActiveRoundMenu();
        }

        private void OnPlayerNameEndEdit(string value)
        {
            value = value.Trim();
            if (value.Length < 2) value = "Player";
            if (value.Length > 24) value = value[..24];
            playerNameInput.SetTextWithoutNotify(value);
            UnityEngine.PlayerPrefs.SetString("PlayerName", value);
            UnityEngine.PlayerPrefs.Save();
            if (value == _lastSyncedName) return;
            _lastSyncedName = value;
            LocalPlayerRegistry.Current?.SetNameServerRpc(value);
            RequestUiRefresh();
        }

        private void DetectDisconnection()
        {
            var nm = NetworkManager.Singleton;
            var connected = nm != null && nm.IsListening;

            if (_wasConnected && !connected)
            {
                // Connection just dropped (host left, kicked, or shutdown). The local player
                // is gone, so its Esc handler can't unlock the cursor — do it here so the menu
                // is reachable.
                _lateJoinLoading = false;
                activeMenuOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RequestUiRefresh();
            }
            else if (!_wasConnected && connected)
            {
                activeMenuOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                RequestUiRefresh();
            }

            _wasConnected = connected;
        }

        private bool IsBlockingGameplayInput()
        {
            // Dev tool: F1 blueprint editor freezes gameplay input + cursor in 2D
            // edit mode. In 3D walk mode the player needs FPS controls so we let
            // gameplay input through.
            if (FriendSlop.Interiors.Blueprints.BlueprintEditorController.IsBlockingInput) return true;
            if (_chatInputFocused) return true;
            if (chatInput != null && chatInput.isFocused) return true;
            if (playerNameInput != null && playerNameInput.isFocused) return true;
            if (joinInput != null && joinInput.isFocused) return true;

            var round = RoundManagerRegistry.Current;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            if (phase == RoundPhase.Loading || phase == RoundPhase.Transitioning || _lateJoinLoading) return true;

            var networkManager = NetworkManager.Singleton;
            var connected = networkManager != null && networkManager.IsListening;
            if (!connected) return true;

            return !RoundStateUtility.AllowsGameplayInput(phase) || activeMenuOpen;
        }

        // ── RefreshUi ─────────────────────────────────────────────────────────

        private void RefreshUi()
        {
            var networkManager = NetworkManager.Singleton;
            var session = SessionManager;
            var round = RoundManagerRegistry.Current;
            var connected = networkManager != null && networkManager.IsListening;
            var isHost = networkManager != null && networkManager.IsHost;
            var canCancelSessionOperation = session != null && session.CanCancelSessionOperation;
            var connectionInProgress = session != null && session.IsSessionOperationInProgress;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var activeRound = connected && phase == RoundPhase.Active;
            var gameplayPhase = connected && RoundStateUtility.AllowsGameplayInput(phase);
            var isLoading = phase == RoundPhase.Loading || phase == RoundPhase.Transitioning || _lateJoinLoading;

            if (!_hasObservedRoundPhase || _lastObservedRoundPhase != phase)
            {
                if (phase == RoundPhase.Loading || phase == RoundPhase.Active || phase == RoundPhase.Transitioning)
                    activeMenuOpen = false;
                else if (RoundStateUtility.IsShipPhase(phase))
                    activeMenuOpen = false;

                _lastObservedRoundPhase = phase;
                _hasObservedRoundPhase = true;
            }

            if (!gameplayPhase)
                activeMenuOpen = false;

            // Blueprint editor 2D mode takes precedence — cursor freed regardless of
            // round phase or menu state. 3D walk mode lets cursor lock normally.
            bool blueprintEditorActive = FriendSlop.Interiors.Blueprints.BlueprintEditorController.IsBlockingInput;
            var showMenu = !isLoading && (!gameplayPhase || activeMenuOpen || blueprintEditorActive);

            if (showMenu && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (gameplayPhase && !activeMenuOpen && !blueprintEditorActive
                     && (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            UpdateLoadingScreen(isLoading, phase, round);

            if (menuBackdropImage != null)
            {
                menuBackdropImage.gameObject.SetActive(showMenu);
                menuBackdropImage.color = connected
                    ? new Color(0.01f, 0.02f, 0.02f, 0.52f)
                    : new Color(0.01f, 0.02f, 0.02f, 0.68f);
            }

            menuRoot.SetActive(showMenu);
            if (namePanelRoot != null) namePanelRoot.SetActive(showMenu);
            if (gameOverText != null)
                gameOverText.gameObject.SetActive(showMenu && phase == RoundPhase.AllDead);
            if (moneyPanelRect != null)
                moneyPanelRect.gameObject.SetActive(activeRound);
            if (timerText != null)
                timerText.gameObject.SetActive(activeRound);
            LayoutHud();

            titleText.text = "FRIEND SLOP RETRIEVAL";
            statusText.text = BuildStatusText(session, round, connectionInProgress);
            statusText.color = connectionInProgress
                ? new Color(0.72f, 0.9f, 1f, 1f)
                : Color.white;
            if (connectionHintText != null)
            {
                connectionHintText.gameObject.SetActive(!connected && connectionInProgress);
                if (!connected && connectionInProgress)
                    connectionHintText.text = BuildConnectionHint(session);
            }
            UpdateJoinCodePanel(session, showMenu, connected, isHost);

            lobbyQueueText.text = BuildLobbyQueue(networkManager);

            hostButton.gameObject.SetActive(!connected && !canCancelSessionOperation);
            joinButton.gameObject.SetActive(!connected && !canCancelSessionOperation);
            localHostButton.gameObject.SetActive(!connected && !canCancelSessionOperation);
            localJoinButton.gameObject.SetActive(!connected && !canCancelSessionOperation);
            joinInput.gameObject.SetActive(!connected && !canCancelSessionOperation);
            cancelButton.gameObject.SetActive(!connected && canCancelSessionOperation);
            SetButtonLabel(cancelButton, "Cancel Connection");
            SetButtonColor(cancelButton, canCancelSessionOperation ? CancelButtonColor : DefaultButtonColor);
            startButton.gameObject.SetActive(connected && isHost && phase == RoundPhase.Lobby);
            restartButton.gameObject.SetActive(connected && isHost &&
                (phase == RoundPhase.Success || phase == RoundPhase.Failed || phase == RoundPhase.AllDead));
            UpdateRestartButtonLabel(round, phase);
            UpdateCyclePlanetButton(round, phase, connected, isHost);
            shutdownButton.gameObject.SetActive(connected);
            SetButtonLabel(shutdownButton, canCancelSessionOperation && !isHost ? "Cancel Join" : "Leave Session");
            lobbyQueueText.gameObject.SetActive(connected && !canCancelSessionOperation);

            LayoutMenu(connected, isHost, phase);

            if (round != null)
            {
                RefreshHudText(round);
                resultText.text = phase switch
                {
                    RoundPhase.Lobby => connected
                        ? $"Ship Lobby: walk around or wait for launch.\nCurrent planet: {FormatPlanetLabel(round.CurrentPlanet, round.Catalog)}"
                        : "Host or join to begin.",
                    RoundPhase.Active => string.Empty,
                    RoundPhase.Success => BuildSuccessResultText(round),
                    RoundPhase.Failed => BuildFailureResultText(round),
                    RoundPhase.AllDead => $"WIPE OUT - everyone died.\nBack aboard with ${round.CollectedValue.Value} collected.",
                    _ => string.Empty
                };
            }
            else
            {
                RefreshHudText(null);
                resultText.text = connected ? string.Empty : "Host or join to begin.";
            }

            RefreshPlayerHud(connected, phase, gameplayPhase, activeRound);
        }

        // ── Cursor / menu-state helpers ───────────────────────────────────────

        public void EnterGameplayMode() => LockGameplayCursor();

        private void LockGameplayCursor()
        {
            activeMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            RequestUiRefresh();
        }

        private void OpenActiveRoundMenu()
        {
            activeMenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RequestUiRefresh();
        }

        private void HandleSessionExitButton()
        {
            var session = SessionManager;
            if (session == null) return;

            if (session.CanCancelSessionOperation)
                session.CancelSessionOperation();
            else
                session.Shutdown();
        }

        public void UnlockMenuCursor()
        {
            activeMenuOpen = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RequestUiRefresh();
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
