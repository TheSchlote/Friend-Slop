using FriendSlop.Round;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    public partial class FriendSlopUI
    {
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const float MinMenuWidth = 460f;
        private const float MaxMenuWidth = 620f;
        private const float MinDisconnectedMenuHeight = 540f;
        private const float MaxDisconnectedMenuHeight = 640f;
        private const float MinConnectedMenuHeight = 480f;
        private const float MaxConnectedMenuHeight = 580f;
        private const float MinButtonWidth = 300f;
        private const float MaxButtonWidth = 380f;
        private const float MinButtonHeight = 42f;
        private const float MaxButtonHeight = 52f;
        private static readonly Color DefaultButtonColor = new(0.16f, 0.18f, 0.18f, 0.96f);
        private static readonly Color CancelButtonColor = new(0.48f, 0.17f, 0.12f, 0.96f);
        private static readonly Color SuccessButtonColor = new(0.16f, 0.42f, 0.24f, 0.96f);

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
        private bool _loadingProgressActive;
        private RoundPhase _loadingProgressPhase;
        private float _loadingProgressStartTime;
        private float _loadingProgressValue;
        private const float LateJoinLoadingDuration = 3f;
        private const float TransitionProgressFillSeconds = 4f;
        private const float TransitionProgressMax = 0.92f;

        private Image _sunGlareImage;
        private Image _fadeOverlayImage;
        private float _fadeAlpha;
        private const float FadeSpeed = 2.2f;
        private float _teleporterFlashStartTime = -1f;
        private const float TeleporterFlashAttackSeconds = 0.06f;
        private const float TeleporterFlashHoldSeconds = 0.05f;
        private const float TeleporterFlashReleaseSeconds = 0.32f;

        private Text _objectiveBannerText;
        private float _objectiveBannerAlpha;
        private bool _objectiveBannerLatched;
        private const float ObjectiveBannerFadeSpeed = 3.2f;
        private const float ObjectiveBannerPulseSpeed = 3.4f;

        private RectTransform chargePanelRect;
        private RectTransform chargeFillRect;
        private Image chargeFillImage;

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

        private const float FullUiRefreshInterval = 0.25f;
        private bool _uiRefreshRequested = true;
        private RoundManager _observedUiRound;
        private bool _hasUiRefreshSnapshot;
        private bool _lastRefreshConnected;
        private bool _lastRefreshIsHost;
        private bool _lastRefreshCanCancelSessionOperation;
        private bool _lastRefreshConnectionInProgress;
        private bool _lastRefreshActiveMenuOpen;
        private bool _lastRefreshLateJoinLoading;
        private int _lastRefreshConnectedClientCount = -1;
        private RoundPhase _lastRefreshPhase = RoundPhase.Lobby;
        private float _nextTimedUiRefreshTime;
    }
}
