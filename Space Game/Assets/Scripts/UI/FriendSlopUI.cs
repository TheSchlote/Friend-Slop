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

        public static FriendSlopUI Instance { get; private set; }
        public static bool BlocksGameplayInput => Instance != null && Instance.IsBlockingGameplayInput();

        private Canvas canvas;
        private RectTransform canvasRect;
        private RectTransform menuRect;
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
        private Image _fadeOverlayImage;
        private float _fadeAlpha;
        private const float FadeSpeed = 2.2f; // alpha units per second
        private RectTransform chargePanelRect;
        private RectTransform chargeFillRect;
        private Image chargeFillImage;
        private GameObject menuRoot;
        private Text titleText;
        private Text statusText;
        private Text lobbyQueueText;
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
        private Button startButton;
        private Button restartButton;
        private Text restartButtonLabel;
        private Button cyclePlanetButton;
        private Text cyclePlanetButtonLabel;
        private Button shutdownButton;
        private Button quitButton;
        private bool menuPinned;
        private bool _wasConnected;
        private Font font;

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
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                menuPinned = !menuPinned;
            }

            DetectDisconnection();

            if (_lateJoinLoading && Time.time - _lateJoinLoadingStartTime >= LateJoinLoadingDuration)
                _lateJoinLoading = false;

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
                menuPinned = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // While disconnected, ensure Esc still toggles the cursor as a safety net.
            if (!connected && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                var shouldLock = Cursor.lockState != CursorLockMode.Locked;
                Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !shouldLock;
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

            var activeRound = round != null && phase == RoundPhase.Active;
            return !activeRound || menuPinned || Cursor.lockState != CursorLockMode.Locked;
        }

        private void RefreshUi()
        {
            var networkManager = NetworkManager.Singleton;
            var session = NetworkSessionManager.Instance;
            var round = RoundManager.Instance;
            var connected = networkManager != null && networkManager.IsListening;
            var isHost = networkManager != null && networkManager.IsHost;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var activeRound = connected && phase == RoundPhase.Active;
            var isLoading = phase == RoundPhase.Loading || phase == RoundPhase.Transitioning || _lateJoinLoading;
            var showMenu = !isLoading && (!activeRound || menuPinned || Cursor.lockState != CursorLockMode.Locked);

            UpdateLoadingScreen(isLoading, phase, round);

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
            if (session != null)
            {
                var code = string.IsNullOrWhiteSpace(session.LastJoinCode) ? string.Empty : $"\nCode: {session.LastJoinCode}";
                statusText.text = session.Status + code + $"\nTeam Money: ${GetTeamMoney(round)}\nTab toggles this menu. Esc unlocks mouse.";
            }
            else
            {
                statusText.text = "Not connected.\nTeam Money: $0\nTab toggles this menu. Esc unlocks mouse.";
            }

            lobbyQueueText.text = BuildLobbyQueue(networkManager);

            hostButton.gameObject.SetActive(!connected);
            joinButton.gameObject.SetActive(!connected);
            localHostButton.gameObject.SetActive(!connected);
            localJoinButton.gameObject.SetActive(!connected);
            joinInput.gameObject.SetActive(!connected);
            startButton.gameObject.SetActive(connected && isHost && phase == RoundPhase.Lobby);
            restartButton.gameObject.SetActive(connected && isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed || phase == RoundPhase.AllDead));
            UpdateRestartButtonLabel(round, phase);
            UpdateCyclePlanetButton(round, phase, connected, isHost);
            shutdownButton.gameObject.SetActive(connected);
            lobbyQueueText.gameObject.SetActive(connected);

            LayoutMenu(connected, isHost, phase);

            if (round != null)
            {
                quotaText.text = $"Team Money: ${round.CollectedValue.Value}";
                timerText.text = BuildObjectiveHudText(round);
                resultText.text = phase switch
                {
                    RoundPhase.Lobby => connected ? $"Lobby: host starts the run when everyone is in.\nCurrent planet: {FormatPlanetLabel(round.CurrentPlanet)}" : "Host or join to begin.",
                    RoundPhase.Active => string.Empty,
                    RoundPhase.Success => BuildSuccessResultText(round),
                    RoundPhase.Failed => "FAILED: the timer ate your paycheck.",
                    RoundPhase.AllDead => $"WIPE OUT — everyone died.\nCollected ${round.CollectedValue.Value} toward quota.",
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
            var menuWidth = Mathf.Clamp(canvasSize.x * 0.34f, MinMenuWidth, MaxMenuWidth);
            var menuHeight = connected
                ? Mathf.Clamp(canvasSize.y * 0.44f, MinConnectedMenuHeight, MaxConnectedMenuHeight)
                : Mathf.Clamp(canvasSize.y * 0.58f, MinDisconnectedMenuHeight, MaxDisconnectedMenuHeight);
            var contentWidth = menuWidth - 64f;
            var buttonWidth = Mathf.Clamp(menuWidth * 0.64f, MinButtonWidth, MaxButtonWidth);
            var buttonHeight = Mathf.Clamp(canvasSize.y * 0.045f, MinButtonHeight, MaxButtonHeight);
            var buttonGap = Mathf.Clamp(canvasSize.y * 0.014f, 12f, 18f);

            menuRect.sizeDelta = new Vector2(menuWidth, menuHeight);
            SetSize(titleText.rectTransform, new Vector2(contentWidth, 38f));
            SetSize(statusText.rectTransform, new Vector2(contentWidth, 88f));
            SetSize(lobbyQueueText.rectTransform, new Vector2(contentWidth, 96f));
            SetButtonSize(hostButton, buttonWidth, buttonHeight);
            SetButtonSize(joinButton, buttonWidth, buttonHeight);
            SetButtonSize(localHostButton, buttonWidth, buttonHeight);
            SetButtonSize(localJoinButton, buttonWidth, buttonHeight);
            SetButtonSize(startButton, buttonWidth, buttonHeight);
            SetButtonSize(restartButton, buttonWidth, buttonHeight);
            SetButtonSize(cyclePlanetButton, buttonWidth, buttonHeight);
            SetButtonSize(shutdownButton, buttonWidth, buttonHeight);
            SetButtonSize(quitButton, buttonWidth, buttonHeight);
            SetSize(joinInput.GetComponent<RectTransform>(), new Vector2(buttonWidth, buttonHeight));

            if (!connected)
            {
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

            var primaryButtonY = -48f;
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

            menuRoot = CreatePanel("Menu", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MaxMenuWidth, MinDisconnectedMenuHeight), new Color(0.04f, 0.05f, 0.05f, 0.96f));
            menuRect = menuRoot.GetComponent<RectTransform>();
            titleText = CreateText("Title", menuRoot.transform, "FRIEND SLOP RETRIEVAL", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(460f, 34f));
            statusText = CreateText("Status", menuRoot.transform, "Not connected.", 14, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(440f, 72f));
            lobbyQueueText = CreateText("LobbyQueue", menuRoot.transform, string.Empty, 15, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(440f, 82f));

            joinInput = CreateInput("JoinInput", menuRoot.transform, "Relay code or LAN IP", new Vector2(0f, 68f));
            hostButton = CreateButton("Host Online", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.HostOnline());
            joinButton = CreateButton("Join Code", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.JoinOnline(joinInput.text));
            localHostButton = CreateButton("Host LAN", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.StartLocalHost());
            localJoinButton = CreateButton("Join LAN", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.StartLocalClient(string.IsNullOrWhiteSpace(joinInput.text) ? "127.0.0.1" : joinInput.text));
            startButton = CreateButton("Start Round", menuRoot.transform, Vector2.zero, () =>
            {
                RoundManager.Instance?.RequestStartRoundServerRpc();
                LockGameplayCursor();
            });
            restartButton = CreateButton("Restart Round", menuRoot.transform, Vector2.zero, OnRestartOrTravelClicked);
            restartButtonLabel = restartButton.GetComponentInChildren<Text>();
            cyclePlanetButton = CreateButton("Cycle Planet", menuRoot.transform, Vector2.zero, OnCyclePlanetClicked);
            cyclePlanetButtonLabel = cyclePlanetButton.GetComponentInChildren<Text>();
            shutdownButton = CreateButton("Leave Session", menuRoot.transform, Vector2.zero, () => NetworkSessionManager.Instance?.Shutdown());
            quitButton = CreateButton("Quit", menuRoot.transform, Vector2.zero, QuitGame);

            // Loading screen — added last so it renders on top of all other UI
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

            // Full-screen black overlay for planet transition fades — rendered last so it sits above everything.
            var fadeObj = new GameObject("PlanetFadeOverlay");
            fadeObj.transform.SetParent(canvasObject.transform, false);
            var fadeRect = fadeObj.AddComponent<RectTransform>();
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = Vector2.zero;
            fadeRect.offsetMax = Vector2.zero;
            _fadeOverlayImage = fadeObj.AddComponent<Image>();
            _fadeOverlayImage.color = new Color(0f, 0f, 0f, 0f);
            _fadeOverlayImage.raycastTarget = false;
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
            var buttonObject = CreatePanel(label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(290f, 42f), new Color(0.16f, 0.18f, 0.18f, 0.96f));
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

        private void OnRestartOrTravelClicked()
        {
            var rm = RoundManager.Instance;
            if (rm == null) return;

            if (rm.Phase.Value == RoundPhase.Success && rm.GetNextTierCandidates().Count > 0 && !rm.HasReachedFinalTier)
                rm.RequestTravelToNextPlanetServerRpc();
            else
                rm.RequestRestartRoundServerRpc();

            LockGameplayCursor();
        }

        private void OnCyclePlanetClicked()
        {
            var rm = RoundManager.Instance;
            if (rm == null || rm.Catalog == null) return;
            var candidates = rm.GetNextTierCandidates();
            if (candidates.Count <= 1) return;

            var current = rm.SelectedNextPlanet;
            var idx = current != null ? candidates.IndexOf(current) : -1;
            var nextPlanet = candidates[(idx + 1) % candidates.Count];
            var catalogIndex = rm.Catalog.IndexOf(nextPlanet);
            if (catalogIndex >= 0)
                rm.RequestSelectNextPlanetServerRpc(catalogIndex);
        }

        private static string FormatPlanetLabel(PlanetDefinition planet)
        {
            return planet != null ? $"{planet.DisplayName} (Tier {planet.Tier})" : "Unknown";
        }

        private static string BuildSuccessResultText(RoundManager round)
        {
            if (round == null) return "ROCKET ASSEMBLED.";

            var current = FormatPlanetLabel(round.CurrentPlanet);
            if (round.HasReachedFinalTier)
                return $"ROCKET ASSEMBLED on {current}.\nFinal tier reached — replay to keep grinding.";

            var candidates = round.GetNextTierCandidates();
            if (candidates.Count == 0)
                return $"ROCKET ASSEMBLED on {current}.\nNo tier {round.NextTier} planets registered yet — host can replay.";

            var next = round.SelectedNextPlanet;
            if (next == null || next.Tier != round.NextTier)
                next = candidates[0];

            var optionsLine = candidates.Count > 1
                ? $"\n{candidates.Count} tier {round.NextTier} options — host can cycle."
                : string.Empty;
            return $"ROCKET ASSEMBLED on {current}.\nNext: {FormatPlanetLabel(next)}{optionsLine}";
        }

        private void UpdateRestartButtonLabel(RoundManager round, RoundPhase phase)
        {
            if (restartButtonLabel == null) return;
            if (round != null && phase == RoundPhase.Success && !round.HasReachedFinalTier)
            {
                var candidates = round.GetNextTierCandidates();
                if (candidates.Count > 0)
                {
                    var next = round.SelectedNextPlanet;
                    if (next == null || next.Tier != round.NextTier) next = candidates[0];
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
                       && !round.HasReachedFinalTier && round.GetNextTierCandidates().Count > 1;
            cyclePlanetButton.gameObject.SetActive(show);
            if (show && cyclePlanetButtonLabel != null)
                cyclePlanetButtonLabel.text = $"Cycle Tier {round.NextTier} Planet";
        }

        private void LockGameplayCursor()
        {
            menuPinned = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
        }

        private void UpdateLoadingScreen(bool isLoading, RoundPhase phase, RoundManager round)
        {
            if (loadingScreenRoot == null) return;
            loadingScreenRoot.SetActive(isLoading);
            if (!isLoading) return;

            float progress;
            string statusMsg;
            if (phase == RoundPhase.Transitioning && round != null)
            {
                var dest = round.SelectedNextPlanet ?? round.CurrentPlanet;
                var destName = dest != null ? dest.DisplayName : "Unknown";
                statusMsg = $"Traveling to {destName}...";
                // Pulse the bar back and forth as an indeterminate indicator.
                progress = Mathf.PingPong(Time.time * 0.6f, 1f);
            }
            else if (phase == RoundPhase.Loading && round != null)
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
            if (round != null && (round.Phase.Value == RoundPhase.Loading || round.Phase.Value == RoundPhase.Transitioning)) isLoading = true;
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
            // Glare begins at ~18° off-center and peaks at dead center (~2°)
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
            var round = RoundManager.Instance;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var targetAlpha = phase == RoundPhase.Transitioning ? 1f : 0f;
            _fadeAlpha = Mathf.MoveTowards(_fadeAlpha, targetAlpha, FadeSpeed * Time.deltaTime);
            _fadeOverlayImage.color = new Color(0f, 0f, 0f, _fadeAlpha);
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
