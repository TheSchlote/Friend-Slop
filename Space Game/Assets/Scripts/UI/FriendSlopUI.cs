using FriendSlop.Core;
using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // Core state, lifecycle, and the main RefreshUi loop.
    // Canvas construction  →  FriendSlopUI.BuildUi.cs
    // HUD bars / overlays  →  FriendSlopUI.Hud.cs
    // Menu layout + text   →  FriendSlopUI.Menu.cs
    public partial class FriendSlopUI : MonoBehaviour
    {
        // ── Layout constants ──────────────────────────────────────────────────
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float MinMenuWidth = 460f;
        private const float MaxMenuWidth = 620f;
        private const float MinDisconnectedMenuHeight = 540f;
        private const float MaxDisconnectedMenuHeight = 640f;
        private const float MinConnectedMenuHeight = 400f;
        private const float MaxConnectedMenuHeight = 500f;
        private const float MinButtonWidth = 300f;
        private const float MaxButtonWidth = 380f;
        private const float MinButtonHeight = 42f;
        private const float MaxButtonHeight = 52f;
        private static readonly Color DefaultButtonColor = new Color(0.16f, 0.18f, 0.18f, 0.96f);
        private static readonly Color CancelButtonColor = new Color(0.48f, 0.17f, 0.12f, 0.96f);
        private static readonly Color SuccessButtonColor = new Color(0.16f, 0.42f, 0.24f, 0.96f);

        // ── Singleton ─────────────────────────────────────────────────────────
        public static FriendSlopUI Instance { get; private set; }
        public static bool BlocksGameplayInput => Instance != null && Instance.IsBlockingGameplayInput();

        // ── Canvas roots ──────────────────────────────────────────────────────
        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform menuRect;
        private RectTransform namePanelRect;
        private RectTransform hudRect;
        private RectTransform moneyPanelRect;

        // ── Stamina bar ───────────────────────────────────────────────────────
        private RectTransform staminaPanelRect;
        private RectTransform staminaFillRect;
        private Image staminaFillImage;

        // ── Health bar ────────────────────────────────────────────────────────
        private RectTransform healthPanelRect;
        private RectTransform healthFillRect;
        private Image healthFillImage;
        private Text healthLabelText;

        // ── Death / damage ────────────────────────────────────────────────────
        private Text deathOverlayText;
        private Text gameOverText;
        private Image damageFlashImage;
        private float _damageFlashAlpha;

        // ── Loading screen ────────────────────────────────────────────────────
        private GameObject loadingScreenRoot;
        private Text loadingStatusText;
        private RectTransform loadingBarFillRect;
        private bool _lateJoinLoading;
        private float _lateJoinLoadingStartTime;
        private const float LateJoinLoadingDuration = 3f;

        // ── Environment effects ───────────────────────────────────────────────
        private Image _sunGlareImage;
        private DayNightCycle _dayNightCycle;
        private Image _fadeOverlayImage;
        private float _fadeAlpha;
        private const float FadeSpeed = 2.2f;

        // ── Charge bar ────────────────────────────────────────────────────────
        private RectTransform chargePanelRect;
        private RectTransform chargeFillRect;
        private Image chargeFillImage;

        // ── Inventory slots ───────────────────────────────────────────────────
        private RectTransform inventoryPanelRect;
        private Image[] inventorySlotBackgrounds;
        private Image[] inventorySlotBorders;
        private Text[] inventorySlotItemTexts;
        private Text[] inventorySlotValueTexts;
        private RawImage[] inventorySlotPreviews;
        private InventoryPreviewRig inventoryPreviewRig;
        private static readonly Color InventorySlotEmptyColor = new(0.04f, 0.05f, 0.06f, 0.78f);
        private static readonly Color InventorySlotFilledColor = new(0.10f, 0.13f, 0.16f, 0.86f);
        private static readonly Color InventorySlotActiveColor = new(0.18f, 0.34f, 0.46f, 0.94f);
        private static readonly Color InventorySlotBorderIdleColor = new(0.18f, 0.20f, 0.22f, 0.85f);
        private static readonly Color InventorySlotBorderActiveColor = new(0.95f, 0.78f, 0.18f, 1f);

        // ── Menu elements ─────────────────────────────────────────────────────
        private Image menuBackdropImage;
        private GameObject menuRoot;
        private Text titleText;
        private Text statusText;
        private GameObject joinCodePanelRoot;
        private RectTransform joinCodePanelRect;
        private Text joinCodeLabelText;
        private Text joinCodeText;
        private Button copyCodeButton;
        private Text copyCodeButtonText;
        private Text lobbyQueueText;
        private Text connectionHintText;
        private Text quotaText;
        private Text timerText;
        private Text promptText;
        private Text carriedText;
        private Text resultText;
        private InputField joinInput;
        private InputField playerNameInput;
        private GameObject namePanelRoot;
        private string _lastSyncedName = string.Empty;
        private Button hostButton;
        private Button joinButton;
        private Button localHostButton;
        private Button localJoinButton;
        private Button cancelButton;
        private Button startButton;
        private Button restartButton;
        private Text restartButtonLabel;
        private Button cyclePlanetButton;
        private Text cyclePlanetButtonLabel;
        private Button shutdownButton;
        private Button quitButton;
        private bool activeMenuOpen;
        private bool _wasConnected;
        private bool _hasObservedRoundPhase;
        private RoundPhase _lastObservedRoundPhase = RoundPhase.Lobby;
        private Font font;
        private float _copyCodeFeedbackUntil;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
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
            GameplayInputState.RegisterBlockProvider(IsBlockingGameplayInput);

            NetworkSessionManager.SessionEnded += HandleSessionEnded;
            NetworkFirstPersonController.LocalPlayerDamaged += HandleLocalPlayerDamaged;
            NetworkFirstPersonController.LocalPlayerJoinedActiveRound += HandleLocalPlayerJoinedActiveRound;
        }

        private void OnDisable()
        {
            NetworkSessionManager.SessionEnded -= HandleSessionEnded;
            NetworkFirstPersonController.LocalPlayerDamaged -= HandleLocalPlayerDamaged;
            NetworkFirstPersonController.LocalPlayerJoinedActiveRound -= HandleLocalPlayerJoinedActiveRound;

            // Only clear if we're the registered provider; if a fresh UI instance has already
            // taken over (e.g. scene reload during a host restart) leave its provider in place.
            if (Instance == this)
                GameplayInputState.ClearBlockProvider();
        }

        private void HandleSessionEnded() => UnlockMenuCursor();
        private void HandleLocalPlayerDamaged() => ShowDamageFlash();
        private void HandleLocalPlayerJoinedActiveRound() => ShowLateJoinLoading();

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            HandleMenuHotkeys();
            DetectDisconnection();

            if (_lateJoinLoading && Time.time - _lateJoinLoadingStartTime >= LateJoinLoadingDuration)
            {
                _lateJoinLoading = false;
                if (IsActiveRound())
                    LockGameplayCursor();
            }

            UpdateSunGlare();

            if (_damageFlashAlpha > 0f)
            {
                _damageFlashAlpha = Mathf.Max(0f, _damageFlashAlpha - Time.deltaTime * 1.8f);
                if (damageFlashImage != null)
                    damageFlashImage.color = new Color(0.72f, 0f, 0f, _damageFlashAlpha);
            }

            RefreshUi();
            UpdateFade();
        }

        private void HandleMenuHotkeys()
        {
            if (Keyboard.current == null) return;

            var menuTogglePressed = Keyboard.current.tabKey.wasPressedThisFrame ||
                                    Keyboard.current.escapeKey.wasPressedThisFrame;
            if (!menuTogglePressed) return;

            var networkManager = NetworkManager.Singleton;
            var round = RoundManager.Instance;
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
            NetworkFirstPersonController.LocalPlayer?.SetNameServerRpc(value);
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
            }
            else if (!_wasConnected && connected)
            {
                activeMenuOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            _wasConnected = connected;
        }

        private bool IsBlockingGameplayInput()
        {
            if (playerNameInput != null && playerNameInput.isFocused) return true;
            if (joinInput != null && joinInput.isFocused) return true;

            var round = RoundManager.Instance;
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
            var session = NetworkSessionManager.Instance;
            var round = RoundManager.Instance;
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

            var showMenu = !isLoading && (!gameplayPhase || activeMenuOpen);

            if (showMenu && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (gameplayPhase && !activeMenuOpen && (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
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
                quotaText.text = $"Team Money: ${round.CollectedValue.Value}";
                timerText.text = BuildObjectiveHudText(round);
                resultText.text = phase switch
                {
                    RoundPhase.Lobby => connected
                        ? $"Ship Lobby: walk around or wait for launch.\nCurrent planet: {FormatPlanetLabel(round.CurrentPlanet)}"
                        : "Host or join to begin.",
                    RoundPhase.Active => string.Empty,
                    RoundPhase.Success => BuildSuccessResultText(round),
                    RoundPhase.Failed => "FAILED: back aboard the ship.\nHost can restart the planet run.",
                    RoundPhase.AllDead => $"WIPE OUT - everyone died.\nBack aboard with ${round.CollectedValue.Value} collected.",
                    _ => string.Empty
                };
            }
            else
            {
                quotaText.text = "Team Money: $0";
                timerText.text = "Parts: Cockpit missing | Wings missing | Engine missing";
                resultText.text = connected ? string.Empty : "Host or join to begin.";
            }

            var localPlayer = NetworkFirstPersonController.LocalPlayer;
            if (localPlayer != null)
            {
                promptText.text = localPlayer.Interactor != null ? localPlayer.Interactor.CurrentPrompt : string.Empty;
                carriedText.text = localPlayer.HeldItem != null
                    ? $"Carrying: {localPlayer.HeldItem.ItemName} (${localPlayer.HeldItem.Value})"
                    : string.Empty;
            }
            else
            {
                promptText.text = string.Empty;
                carriedText.text = string.Empty;
            }

            UpdateStaminaBar(localPlayer, gameplayPhase);
            UpdateHealthBar(localPlayer, gameplayPhase);
            UpdateDeathOverlay(localPlayer, activeRound);
            UpdateChargeBar(localPlayer, gameplayPhase);
            UpdateInventoryHud(localPlayer, activeRound);
        }

        // ── Cursor / menu-state helpers ───────────────────────────────────────

        public void EnterGameplayMode() => LockGameplayCursor();

        private void LockGameplayCursor()
        {
            activeMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OpenActiveRoundMenu()
        {
            activeMenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void HandleSessionExitButton()
        {
            var session = NetworkSessionManager.Instance;
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
