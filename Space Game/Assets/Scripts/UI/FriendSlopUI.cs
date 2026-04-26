using System.Text;
using FriendSlop.Core;
using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    public class FriendSlopUI : MonoBehaviour
    {
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

        public static FriendSlopUI Instance { get; private set; }
        public static bool BlocksGameplayInput => Instance != null && Instance.IsBlockingGameplayInput();

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform menuRect;
        private RectTransform namePanelRect;
        private RectTransform hudRect;
        private RectTransform moneyPanelRect;
        private RectTransform staminaPanelRect;
        private RectTransform staminaFillRect;
        private Image staminaFillImage;
        private RectTransform healthPanelRect;
        private RectTransform healthFillRect;
        private Image healthFillImage;
        private Text healthLabelText;
        private Text deathOverlayText;
        private Text gameOverText;
        private Image damageFlashImage;
        private float _damageFlashAlpha;
        private GameObject loadingScreenRoot;
        private Text loadingStatusText;
        private RectTransform loadingBarFillRect;
        private bool _lateJoinLoading;
        private float _lateJoinLoadingStartTime;
        private const float LateJoinLoadingDuration = 3f;
        private Image _sunGlareImage;
        private DayNightCycle _dayNightCycle;
        private RectTransform chargePanelRect;
        private RectTransform chargeFillRect;
        private Image chargeFillImage;
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
        private Button shutdownButton;
        private Button quitButton;
        private bool activeMenuOpen;
        private Font font;
        private float _copyCodeFeedbackUntil;

        private void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            EnsureEventSystem();
            BuildUi();
        }

        private void Update()
        {
            HandleMenuHotkeys();

            if (_lateJoinLoading && Time.time - _lateJoinLoadingStartTime >= LateJoinLoadingDuration)
            {
                _lateJoinLoading = false;
                if (IsActiveRound())
                {
                    LockGameplayCursor();
                }
            }

            UpdateSunGlare();

            if (_damageFlashAlpha > 0f)
            {
                _damageFlashAlpha = Mathf.Max(0f, _damageFlashAlpha - Time.deltaTime * 1.8f);
                if (damageFlashImage != null)
                    damageFlashImage.color = new Color(0.72f, 0f, 0f, _damageFlashAlpha);
            }

            RefreshUi();
        }

        private void HandleMenuHotkeys()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            var menuTogglePressed = Keyboard.current.tabKey.wasPressedThisFrame ||
                                    Keyboard.current.escapeKey.wasPressedThisFrame;
            if (!menuTogglePressed)
            {
                return;
            }

            var networkManager = NetworkManager.Singleton;
            var round = RoundManager.Instance;
            var connected = networkManager != null && networkManager.IsListening;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var activeRound = connected && phase == RoundPhase.Active;

            if (!activeRound)
            {
                UnlockMenuCursor();
                return;
            }

            if (activeMenuOpen)
            {
                LockGameplayCursor();
            }
            else
            {
                OpenActiveRoundMenu();
            }
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
            var localPlayer = NetworkFirstPersonController.LocalPlayer;
            localPlayer?.SetNameServerRpc(value);
        }

        private bool IsBlockingGameplayInput()
        {
            if (playerNameInput != null && playerNameInput.isFocused) return true;
            if (joinInput != null && joinInput.isFocused) return true;

            var round = RoundManager.Instance;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            if (phase == RoundPhase.Loading || _lateJoinLoading) return true;

            var activeRound = IsActiveRound();
            return !activeRound || activeMenuOpen;
        }

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
            var isLoading = phase == RoundPhase.Loading || _lateJoinLoading;
            if (!activeRound)
            {
                activeMenuOpen = false;
            }

            var showMenu = !isLoading && (!activeRound || activeMenuOpen);

            if (showMenu && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (activeRound && !activeMenuOpen && (Cursor.lockState != CursorLockMode.Locked || Cursor.visible))
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
                {
                    connectionHintText.text = BuildConnectionHint(session);
                }
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
            restartButton.gameObject.SetActive(connected && isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed || phase == RoundPhase.AllDead));
            shutdownButton.gameObject.SetActive(connected);
            SetButtonLabel(shutdownButton, canCancelSessionOperation && !isHost ? "Cancel Join" : "Leave Session");
            lobbyQueueText.gameObject.SetActive(connected && !canCancelSessionOperation);

            LayoutMenu(connected, isHost, phase);

            if (round != null)
            {
                quotaText.text = $"Team Money: ${round.CollectedValue.Value}";
                timerText.text = $"Parts: {FormatPart(round.HasCockpit.Value, "Cockpit")} | {FormatPart(round.HasWings.Value, "Wings")} | {FormatPart(round.HasEngine.Value, "Engine")}";
                resultText.text = phase switch
                {
                    RoundPhase.Lobby => connected ? "Lobby: host starts the planet run when everyone is in." : "Host or join to begin.",
                    RoundPhase.Active => string.Empty,
                    RoundPhase.Success => "ROCKET ASSEMBLED: next planet travel comes later.",
                    RoundPhase.Failed => "FAILED: the timer ate your paycheck.",
                    RoundPhase.AllDead => $"WIPE OUT - everyone died.\nCollected ${round.CollectedValue.Value} toward quota.",
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
            UpdateStaminaBar(localPlayer, activeRound);
            UpdateHealthBar(localPlayer, activeRound);
            UpdateDeathOverlay(localPlayer, activeRound);
            UpdateChargeBar(localPlayer, activeRound);
        }

        private void LayoutMenu(bool connected, bool isHost, RoundPhase phase)
        {
            var canvasSize = GetCanvasSize();
            var showJoinCodePanel = joinCodePanelRoot != null && joinCodePanelRoot.activeSelf;
            var showConnectionHint = connectionHintText != null && connectionHintText.gameObject.activeSelf;
            var menuWidth = Mathf.Clamp(canvasSize.x * 0.34f, MinMenuWidth, MaxMenuWidth);
            var menuHeight = connected
                ? showJoinCodePanel
                    ? Mathf.Clamp(canvasSize.y * 0.58f, 580f, 660f)
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
            {
                SetSize(connectionHintText.rectTransform, new Vector2(contentWidth, 64f));
            }
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
            {
                SetPosition(connectionHintText.rectTransform, new Vector2(0f, showConnectionHint ? 52f : -176f));
            }
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

            var primaryButtonY = showJoinCodePanel ? -126f : -48f;
            var secondaryButtonY = primaryButtonY - buttonHeight - buttonGap;
            var tertiaryButtonY = secondaryButtonY - buttonHeight - buttonGap;
            if (isHost && phase == RoundPhase.Lobby)
            {
                SetPosition(startButton.GetComponent<RectTransform>(), new Vector2(0f, primaryButtonY));
                SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, secondaryButtonY));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, tertiaryButtonY));
                return;
            }

            if (isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed || phase == RoundPhase.AllDead))
            {
                SetPosition(restartButton.GetComponent<RectTransform>(), new Vector2(0f, primaryButtonY));
                SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, secondaryButtonY));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, tertiaryButtonY));
                return;
            }

            SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, primaryButtonY));
            SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, secondaryButtonY));
        }

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

        private void BuildUi()
        {
            var canvasObject = new GameObject("Friend Slop UI");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRect = canvasObject.GetComponent<RectTransform>();

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            // Name input panel — top-right corner, visible whenever the menu is showing
            var namePanel = CreatePanel("NamePanel", canvasObject.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(240f, 68f),
                new Color(0.04f, 0.05f, 0.05f, 0.96f));
            namePanelRoot = namePanel;
            namePanelRect = namePanel.GetComponent<RectTransform>();
            var nameLabel = CreateText("NameLabel", namePanel.transform, "YOUR NAME", 12,
                TextAnchor.UpperCenter, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -6f), new Vector2(-16f, 18f));
            nameLabel.color = new Color(1f, 1f, 1f, 0.65f);
            playerNameInput = CreateInput("PlayerNameInput", namePanel.transform, "Enter your name...", new Vector2(0f, -18f));
            var nameInputRect = playerNameInput.GetComponent<RectTransform>();
            nameInputRect.sizeDelta = new Vector2(220f, 32f);
            nameInputRect.anchoredPosition = new Vector2(0f, -26f);
            var savedName = UnityEngine.PlayerPrefs.GetString("PlayerName", "Player");
            if (savedName.Length < 2) savedName = "Player";
            playerNameInput.text = savedName;
            _lastSyncedName = savedName;
            playerNameInput.onEndEdit.AddListener(OnPlayerNameEndEdit);
            namePanelRoot.SetActive(false);

            var hudRoot = CreatePanel("HUD", canvasObject.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(760f, 180f), new Color(0f, 0f, 0f, 0f));
            var moneyPanel = CreatePanel("TeamMoneyPanel", canvasObject.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(380f, 42f), new Color(0.02f, 0.03f, 0.03f, 0.72f));
            hudRect = hudRoot.GetComponent<RectTransform>();
            moneyPanelRect = moneyPanel.GetComponent<RectTransform>();
            quotaText = CreateText("TeamMoney", moneyPanel.transform, "Team Money: $0", 22, TextAnchor.MiddleRight, Vector2.zero, Vector2.one, new Vector2(-12f, 0f), new Vector2(-24f, 0f));
            var moneyOutline = quotaText.gameObject.AddComponent<Outline>();
            moneyOutline.effectColor = Color.black;
            moneyOutline.effectDistance = new Vector2(2f, -2f);
            timerText = CreateText("Timer", hudRoot.transform, "Parts: Cockpit missing | Wings missing | Engine missing", 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(760f, 32f));
            carriedText = CreateText("Carried", hudRoot.transform, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -38f), new Vector2(620f, 30f));
            resultText = CreateText("Result", hudRoot.transform, string.Empty, 22, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -78f), new Vector2(720f, 42f));

            promptText = CreateText("Prompt", canvasObject.transform, string.Empty, 24, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(760f, 42f));
            CreateText("Reticle", canvasObject.transform, "+", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));

            var staminaPanel = CreatePanel("StaminaPanel", canvasObject.transform,
    new Vector2(0f, 0f), new Vector2(0f, 0f),
    new Vector2(12f, 12f), new Vector2(320f, 22f),
    new Color(0.02f, 0.02f, 0.02f, 0.8f));
            staminaPanelRect = staminaPanel.GetComponent<RectTransform>();

            var staminaFillObject = new GameObject("StaminaFill");
            staminaFillObject.transform.SetParent(staminaPanel.transform, false);
            staminaFillRect = staminaFillObject.AddComponent<RectTransform>();
            staminaFillRect.anchorMin = new Vector2(0f, 0.5f);
            staminaFillRect.anchorMax = new Vector2(0f, 0.5f);
            staminaFillRect.pivot = new Vector2(0f, 0.5f);
            staminaFillRect.anchoredPosition = new Vector2(2f, 0f);
            staminaFillRect.sizeDelta = new Vector2(316f, 18f);
            staminaFillImage = staminaFillObject.AddComponent<Image>();
            staminaFillImage.color = new Color(0.25f, 0.82f, 0.35f, 0.92f);

            var staminaLabel = CreateText("StaminaLabel", staminaPanel.transform, "STAMINA", 13,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var staminaOutline = staminaLabel.gameObject.AddComponent<Outline>();
            staminaOutline.effectColor = Color.black;
            staminaOutline.effectDistance = new Vector2(1f, -1f);

            var healthPanel = CreatePanel("HealthPanel", canvasObject.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 40f), new Vector2(320f, 22f),
                new Color(0.02f, 0.02f, 0.02f, 0.8f));
            healthPanelRect = healthPanel.GetComponent<RectTransform>();

            var healthFillObject = new GameObject("HealthFill");
            healthFillObject.transform.SetParent(healthPanel.transform, false);
            healthFillRect = healthFillObject.AddComponent<RectTransform>();
            healthFillRect.anchorMin = new Vector2(0f, 0.5f);
            healthFillRect.anchorMax = new Vector2(0f, 0.5f);
            healthFillRect.pivot = new Vector2(0f, 0.5f);
            healthFillRect.anchoredPosition = new Vector2(2f, 0f);
            healthFillRect.sizeDelta = new Vector2(316f, 18f);
            healthFillImage = healthFillObject.AddComponent<Image>();
            healthFillImage.color = new Color(0.82f, 0.25f, 0.25f, 0.92f);

            healthLabelText = CreateText("HealthLabel", healthPanel.transform, "HP", 13,
                TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var healthOutline = healthLabelText.gameObject.AddComponent<Outline>();
            healthOutline.effectColor = Color.black;
            healthOutline.effectDistance = new Vector2(1f, -1f);

            deathOverlayText = CreateText("DeathOverlay", canvasObject.transform, string.Empty, 26,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(700f, 90f));
            deathOverlayText.color = new Color(1f, 0.35f, 0.35f, 1f);
            deathOverlayText.gameObject.SetActive(false);

            var chargePanel = CreatePanel("ChargePanel", canvasObject.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -48f), new Vector2(180f, 14f),
                new Color(0.02f, 0.02f, 0.02f, 0.80f));
            chargePanelRect = chargePanel.GetComponent<RectTransform>();

            var chargeFillObject = new GameObject("ChargeFill");
            chargeFillObject.transform.SetParent(chargePanel.transform, false);
            chargeFillRect = chargeFillObject.AddComponent<RectTransform>();
            chargeFillRect.anchorMin = new Vector2(0f, 0.5f);
            chargeFillRect.anchorMax = new Vector2(0f, 0.5f);
            chargeFillRect.pivot = new Vector2(0f, 0.5f);
            chargeFillRect.anchoredPosition = new Vector2(2f, 0f);
            chargeFillRect.sizeDelta = new Vector2(176f, 10f);
            chargeFillImage = chargeFillObject.AddComponent<Image>();
            chargeFillImage.color = new Color(0.92f, 0.78f, 0.18f, 0.92f);

            chargePanelRect.gameObject.SetActive(false);

            // Game over title — big red text above the menu, only shown on AllDead
            gameOverText = CreateText("GameOverTitle", canvasObject.transform, "GAME OVER",
                72, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 230f), new Vector2(700f, 90f));
            gameOverText.color = new Color(0.95f, 0.15f, 0.15f, 1f);
            var gameOverOutline = gameOverText.gameObject.AddComponent<Outline>();
            gameOverOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            gameOverOutline.effectDistance = new Vector2(3f, -3f);
            gameOverText.gameObject.SetActive(false);

            // Damage flash overlay — full-screen red, triggered on hit, fades out over ~0.5s
            var flashObj = new GameObject("DamageFlash");
            flashObj.transform.SetParent(canvasObject.transform, false);
            var flashRect = flashObj.AddComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;
            damageFlashImage = flashObj.AddComponent<Image>();
            damageFlashImage.color = new Color(0.72f, 0f, 0f, 0f);
            damageFlashImage.raycastTarget = false;

            // Sun glare — full-screen warm white, fades in when staring at the sun
            var glareObj = new GameObject("SunGlare");
            glareObj.transform.SetParent(canvasObject.transform, false);
            var glareRect = glareObj.AddComponent<RectTransform>();
            glareRect.anchorMin = Vector2.zero;
            glareRect.anchorMax = Vector2.one;
            glareRect.offsetMin = Vector2.zero;
            glareRect.offsetMax = Vector2.zero;
            _sunGlareImage = glareObj.AddComponent<Image>();
            _sunGlareImage.color = new Color(1f, 0.95f, 0.82f, 0f);
            _sunGlareImage.raycastTarget = false;

            var menuBackdropObject = new GameObject("MenuBackdrop");
            menuBackdropObject.transform.SetParent(canvasObject.transform, false);
            var menuBackdropRect = menuBackdropObject.AddComponent<RectTransform>();
            menuBackdropRect.anchorMin = Vector2.zero;
            menuBackdropRect.anchorMax = Vector2.one;
            menuBackdropRect.offsetMin = Vector2.zero;
            menuBackdropRect.offsetMax = Vector2.zero;
            menuBackdropImage = menuBackdropObject.AddComponent<Image>();
            menuBackdropImage.color = new Color(0.01f, 0.02f, 0.02f, 0.62f);
            menuBackdropImage.raycastTarget = false;
            menuBackdropObject.SetActive(false);

            menuRoot = CreatePanel("Menu", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MaxMenuWidth, MinDisconnectedMenuHeight), new Color(0.04f, 0.05f, 0.05f, 0.96f));
            menuRect = menuRoot.GetComponent<RectTransform>();
            titleText = CreateText("Title", menuRoot.transform, "FRIEND SLOP RETRIEVAL", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(460f, 34f));
            statusText = CreateText("Status", menuRoot.transform, "Not connected.", 14, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(440f, 72f));
            joinCodePanelRoot = CreatePanel("JoinCodePanel", menuRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 76f), new Vector2(380f, 92f),
                new Color(0.01f, 0.015f, 0.015f, 0.94f));
            joinCodePanelRect = joinCodePanelRoot.GetComponent<RectTransform>();
            joinCodeLabelText = CreateText("JoinCodeLabel", joinCodePanelRoot.transform, "JOIN CODE", 12,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 34f), new Vector2(360f, 18f));
            joinCodeLabelText.color = new Color(1f, 1f, 1f, 0.58f);
            joinCodeText = CreateText("JoinCodeText", joinCodePanelRoot.transform, string.Empty, 34,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 10f), new Vector2(360f, 34f));
            joinCodeText.color = new Color(0.92f, 1f, 0.52f, 1f);
            var joinCodeOutline = joinCodeText.gameObject.AddComponent<Outline>();
            joinCodeOutline.effectColor = Color.black;
            joinCodeOutline.effectDistance = new Vector2(2f, -2f);
            copyCodeButton = CreateButton("Copy Code", joinCodePanelRoot.transform, new Vector2(0f, -28f), CopyJoinCodeToClipboard);
            copyCodeButtonText = copyCodeButton.GetComponentInChildren<Text>();
            joinCodePanelRoot.SetActive(false);
            lobbyQueueText = CreateText("LobbyQueue", menuRoot.transform, string.Empty, 15, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(440f, 82f));
            connectionHintText = CreateText("ConnectionHint", menuRoot.transform,
                string.Empty, 15, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 52f), new Vector2(440f, 64f));
            connectionHintText.color = new Color(0.72f, 0.9f, 1f, 0.92f);
            connectionHintText.gameObject.SetActive(false);

            joinInput = CreateInput("JoinInput", menuRoot.transform, "Relay code or LAN IP", new Vector2(0f, 68f));
            hostButton = CreateButton("Host Online", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.HostOnline());
            joinButton = CreateButton("Join Code", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.JoinOnline(joinInput.text));
            localHostButton = CreateButton("Host LAN", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.StartLocalHost());
            localJoinButton = CreateButton("Join LAN", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.StartLocalClient(string.IsNullOrWhiteSpace(joinInput.text) ? "127.0.0.1" : joinInput.text));
            cancelButton = CreateButton("Cancel", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.CancelSessionOperation());
            cancelButton.gameObject.SetActive(false);
            startButton = CreateButton("Start Round", menuRoot.transform, Vector2.zero, () =>
            {
                RoundManager.Instance?.RequestStartRoundServerRpc();
                LockGameplayCursor();
            });
            restartButton = CreateButton("Restart Round", menuRoot.transform, Vector2.zero, () =>
            {
                RoundManager.Instance?.RequestRestartRoundServerRpc();
                LockGameplayCursor();
            });
            shutdownButton = CreateButton("Leave Session", menuRoot.transform, Vector2.zero, HandleSessionExitButton);
            quitButton = CreateButton("Quit", menuRoot.transform, Vector2.zero, QuitGame);

            // Loading screen is added last so it renders on top of all other UI.
            loadingScreenRoot = CreatePanel("LoadingScreen", canvasObject.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.05f, 0.05f, 0.08f, 0.94f));

            CreateText("LoadingTitle", loadingScreenRoot.transform, "LOADING",
                52, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(500f, 70f));

            loadingStatusText = CreateText("LoadingStatus", loadingScreenRoot.transform,
                "Waiting for players...", 20, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 8f), new Vector2(460f, 30f));
            loadingStatusText.color = new Color(1f, 1f, 1f, 0.75f);

            var loadingBarBg = CreatePanel("LoadingBarBg", loadingScreenRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -30f), new Vector2(500f, 20f),
                new Color(0.1f, 0.1f, 0.1f, 0.9f));

            var loadingBarFillObj = new GameObject("LoadingBarFill");
            loadingBarFillObj.transform.SetParent(loadingBarBg.transform, false);
            loadingBarFillRect = loadingBarFillObj.AddComponent<RectTransform>();
            loadingBarFillRect.anchorMin = new Vector2(0f, 0.5f);
            loadingBarFillRect.anchorMax = new Vector2(0f, 0.5f);
            loadingBarFillRect.pivot = new Vector2(0f, 0.5f);
            loadingBarFillRect.anchoredPosition = new Vector2(2f, 0f);
            loadingBarFillRect.sizeDelta = new Vector2(0f, 16f);
            var loadingBarFill = loadingBarFillObj.AddComponent<Image>();
            loadingBarFill.color = new Color(0.3f, 0.65f, 1f, 0.9f);

            loadingScreenRoot.SetActive(false);
            gameOverText.rectTransform.SetAsLastSibling();
            namePanelRoot.transform.SetAsLastSibling();
            loadingScreenRoot.transform.SetAsLastSibling();
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = GetPivotForAnchors(anchorMin, anchorMax);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            if (color.a > 0f)
            {
                var image = panel.AddComponent<Image>();
                image.color = color;
            }

            return panel;
        }

        private Text CreateText(string name, Transform parent, string text, int size, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 rectSize)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = GetPivotForAnchors(anchorMin, anchorMax);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = rectSize;

            var textComponent = textObject.AddComponent<Text>();
            textComponent.font = font;
            textComponent.text = text;
            textComponent.fontSize = size;
            textComponent.alignment = alignment;
            textComponent.color = Color.white;
            textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            return textComponent;
        }

        private Button CreateButton(string label, Transform parent, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObject = CreatePanel(label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(290f, 42f), DefaultButtonColor);
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            CreateText(label + " Text", buttonObject.transform, label, 16, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private InputField CreateInput(string name, Transform parent, string placeholder, Vector2 anchoredPosition)
        {
            var inputObject = CreatePanel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(290f, 42f), new Color(0.02f, 0.02f, 0.02f, 0.95f));
            var input = inputObject.AddComponent<InputField>();
            input.lineType = InputField.LineType.SingleLine;
            var text = CreateText("Text", inputObject.transform, string.Empty, 16, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-24f, 0f));
            var placeholderText = CreateText("Placeholder", inputObject.transform, placeholder, 15, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(12f, 0f), new Vector2(-24f, 0f));
            placeholderText.color = new Color(1f, 1f, 1f, 0.45f);
            input.textComponent = text;
            input.placeholder = placeholderText;
            return input;
        }

        public void EnterGameplayMode()
        {
            LockGameplayCursor();
        }

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
            if (session == null)
            {
                return;
            }

            if (session.CanCancelSessionOperation)
            {
                session.CancelSessionOperation();
            }
            else
            {
                session.Shutdown();
            }
        }

        public void UnlockMenuCursor()
        {
            activeMenuOpen = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void UpdateJoinCodePanel(NetworkSessionManager session, bool showMenu, bool connected, bool isHost)
        {
            if (joinCodePanelRoot == null)
            {
                return;
            }

            var rawCode = session != null ? session.LastJoinCode : string.Empty;
            var copyableCode = GetCopyableJoinCode(rawCode);
            var showJoinCode = showMenu && connected && isHost && !string.IsNullOrWhiteSpace(copyableCode);
            joinCodePanelRoot.SetActive(showJoinCode);
            if (!showJoinCode)
            {
                return;
            }

            joinCodeLabelText.text = IsLanJoinCode(rawCode) ? "LAN ADDRESS - COPY/PASTE" : "JOIN CODE - COPY/PASTE";
            joinCodeText.text = FormatReadableJoinCode(copyableCode);
            var copied = Time.unscaledTime < _copyCodeFeedbackUntil;
            if (copyCodeButtonText != null)
            {
                copyCodeButtonText.text = copied ? "Copied to Clipboard" : "Copy Code";
            }

            SetButtonColor(copyCodeButton, copied ? SuccessButtonColor : DefaultButtonColor);
        }

        private void CopyJoinCodeToClipboard()
        {
            var code = GetCopyableJoinCode(NetworkSessionManager.Instance != null
                ? NetworkSessionManager.Instance.LastJoinCode
                : string.Empty);
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = code;
            _copyCodeFeedbackUntil = Time.unscaledTime + 1.5f;
            if (copyCodeButtonText != null)
            {
                copyCodeButtonText.text = "Copied to Clipboard";
            }

            SetButtonColor(copyCodeButton, SuccessButtonColor);
        }

        private static string GetCopyableJoinCode(string rawCode)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                return string.Empty;
            }

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
            {
                return code;
            }

            code = JoinCodeUtility.NormalizeJoinCode(code);
            var builder = new StringBuilder(code.Length + code.Length / 3);
            for (var i = 0; i < code.Length; i++)
            {
                if (i > 0 && i % 3 == 0)
                {
                    builder.Append(' ');
                }

                builder.Append(code[i]);
            }

            return builder.ToString();
        }

        private static string BuildStatusText(NetworkSessionManager session, RoundManager round, bool connectionInProgress)
        {
            var status = session != null ? session.Status : "Not connected.";
            if (connectionInProgress)
            {
                status = $"{status} {GetActivityDots()}";
            }

            return status + $"\nTeam Money: ${GetTeamMoney(round)}\nTab/Esc toggles menu.";
        }

        private static string BuildConnectionHint(NetworkSessionManager session)
        {
            var remaining = session != null ? session.PendingConnectionSecondsRemaining : 0f;
            if (remaining > 0f)
            {
                return $"Still trying for {Mathf.CeilToInt(remaining)}s.\nCancel returns to this menu safely.";
            }

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

        private static int GetTeamMoney(RoundManager round)
        {
            return round != null ? round.CollectedValue.Value : 0;
        }

        private static Vector2 GetPivotForAnchors(Vector2 anchorMin, Vector2 anchorMax)
        {
            if (anchorMin == anchorMax)
            {
                return anchorMin;
            }

            return new Vector2(0.5f, 0.5f);
        }

        private static void SetPosition(RectTransform rectTransform, Vector2 anchoredPosition)
        {
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = anchoredPosition;
            }
        }

        private Vector2 GetCanvasSize()
        {
            if (canvasRect == null)
            {
                return new Vector2(ReferenceWidth, ReferenceHeight);
            }

            var size = canvasRect.rect.size;
            if (size.x <= 0f || size.y <= 0f)
            {
                return new Vector2(ReferenceWidth, ReferenceHeight);
            }

            return size;
        }

        private static void SetButtonSize(Button button, float width, float height)
        {
            if (button == null)
            {
                return;
            }

            SetSize(button.GetComponent<RectTransform>(), new Vector2(width, height));
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<Text>();
            if (text != null && text.text != label)
            {
                text.text = label;
            }
        }

        private static void SetButtonColor(Button button, Color color)
        {
            if (button == null || button.targetGraphic is not Image image)
            {
                return;
            }

            image.color = color;
        }

        private static void SetSize(RectTransform rectTransform, Vector2 size)
        {
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = size;
            }
        }

        private static string BuildLobbyQueue(NetworkManager networkManager)
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return string.Empty;
            }

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
                {
                    builder.Append(" (you)");
                }
            }

            return builder.ToString();
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
            var round = RoundManager.Instance;
            return networkManager != null &&
                   networkManager.IsListening &&
                   round != null &&
                   round.Phase.Value == RoundPhase.Active;
        }

        private void UpdateLoadingScreen(bool isLoading, RoundPhase phase, RoundManager round)
        {
            if (loadingScreenRoot == null) return;
            loadingScreenRoot.SetActive(isLoading);
            if (!isLoading) return;

            float progress;
            string statusMsg;
            if (phase == RoundPhase.Loading && round != null)
            {
                var ready = round.PlayersReady.Value;
                var expected = Mathf.Max(1, round.PlayersExpectedToLoad.Value);
                progress = (float)ready / expected;
                statusMsg = ready >= expected
                    ? "All players ready!"
                    : $"Waiting for players... ({ready} / {expected})";
            }
            else
            {
                progress = Mathf.Clamp01((Time.time - _lateJoinLoadingStartTime) / LateJoinLoadingDuration);
                statusMsg = "Syncing world...";
            }

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

            // Hide glare during loading or when no camera is active
            var isLoading = _lateJoinLoading;
            var round = RoundManager.Instance;
            if (round != null && round.Phase.Value == RoundPhase.Loading) isLoading = true;
            if (isLoading)
            {
                _sunGlareImage.color = new Color(1f, 0.95f, 0.82f, 0f);
                return;
            }

            var localPlayer = NetworkFirstPersonController.LocalPlayer;
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

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }
    
        private void UpdateStaminaBar(NetworkFirstPersonController localPlayer, bool activeRound)
        {
            if (staminaPanelRect == null)
            {
                return;
            }

            var show = activeRound && localPlayer != null && !localPlayer.IsDeadLocally;
            if (staminaPanelRect.gameObject.activeSelf != show)
            {
                staminaPanelRect.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            var percent = Mathf.Clamp01(localPlayer.StaminaPercent);
            var innerWidth = Mathf.Max(0f, staminaPanelRect.rect.width - 4f);
            staminaFillRect.sizeDelta = new Vector2(innerWidth * percent, 18f);

            if (percent > 0.6f)
            {
                staminaFillImage.color = new Color(0.25f, 0.82f, 0.35f, 0.92f);   // green
            }
            else if (percent > 0.25f)
            {
                staminaFillImage.color = new Color(0.92f, 0.78f, 0.18f, 0.92f);   // yellow
            }
            else
            {
                staminaFillImage.color = new Color(0.92f, 0.24f, 0.18f, 0.92f);   // red
            }
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
