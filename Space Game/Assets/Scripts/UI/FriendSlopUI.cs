using System.Text;
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
        private const float MenuWidth = 500f;
        private const float DisconnectedMenuHeight = 520f;
        private const float ConnectedMenuHeight = 440f;
        private const float HostButtonY = 10f;
        private const float JoinButtonY = -48f;
        private const float LocalHostButtonY = -106f;
        private const float LocalJoinButtonY = -164f;
        private const float StartButtonY = -48f;
        private const float SecondaryButtonY = -106f;
        private const float TertiaryButtonY = -164f;

        public static FriendSlopUI Instance { get; private set; }
        public static bool BlocksGameplayInput => Instance != null && Instance.IsBlockingGameplayInput();

        private Canvas canvas;
        private RectTransform menuRect;
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
        private Button hostButton;
        private Button joinButton;
        private Button localHostButton;
        private Button localJoinButton;
        private Button startButton;
        private Button restartButton;
        private Button shutdownButton;
        private Button quitButton;
        private bool menuPinned;
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

            RefreshUi();
        }

        private bool IsBlockingGameplayInput()
        {
            if (joinInput != null && joinInput.isFocused)
            {
                return true;
            }

            var round = RoundManager.Instance;
            var activeRound = round != null && round.Phase.Value == RoundPhase.Active;
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
            var showMenu = !activeRound || menuPinned || Cursor.lockState != CursorLockMode.Locked;

            menuRoot.SetActive(showMenu);

            titleText.text = "FRIEND SLOP RETRIEVAL";
            if (session != null)
            {
                var code = string.IsNullOrWhiteSpace(session.LastJoinCode) ? string.Empty : $"\nCode: {session.LastJoinCode}";
                statusText.text = session.Status + code + "\nTab toggles this menu. Esc unlocks mouse.";
            }
            else
            {
                statusText.text = "Not connected.\nTab toggles this menu. Esc unlocks mouse.";
            }

            lobbyQueueText.text = BuildLobbyQueue(networkManager);

            hostButton.gameObject.SetActive(!connected);
            joinButton.gameObject.SetActive(!connected);
            localHostButton.gameObject.SetActive(!connected);
            localJoinButton.gameObject.SetActive(!connected);
            joinInput.gameObject.SetActive(!connected);
            startButton.gameObject.SetActive(connected && isHost && phase == RoundPhase.Lobby);
            restartButton.gameObject.SetActive(connected && isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed));
            shutdownButton.gameObject.SetActive(connected);
            lobbyQueueText.gameObject.SetActive(connected);

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
        }

        private void LayoutMenu(bool connected, bool isHost, RoundPhase phase)
        {
            menuRect.sizeDelta = new Vector2(MenuWidth, connected ? ConnectedMenuHeight : DisconnectedMenuHeight);

            if (!connected)
            {
                SetPosition(joinInput.GetComponent<RectTransform>(), new Vector2(0f, 68f));
                SetPosition(hostButton.GetComponent<RectTransform>(), new Vector2(0f, HostButtonY));
                SetPosition(joinButton.GetComponent<RectTransform>(), new Vector2(0f, JoinButtonY));
                SetPosition(localHostButton.GetComponent<RectTransform>(), new Vector2(0f, LocalHostButtonY));
                SetPosition(localJoinButton.GetComponent<RectTransform>(), new Vector2(0f, LocalJoinButtonY));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, -232f));
                return;
            }

            if (isHost && phase == RoundPhase.Lobby)
            {
                SetPosition(startButton.GetComponent<RectTransform>(), new Vector2(0f, StartButtonY));
                SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, SecondaryButtonY));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, TertiaryButtonY));
                return;
            }

            if (isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed))
            {
                SetPosition(restartButton.GetComponent<RectTransform>(), new Vector2(0f, StartButtonY));
                SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, SecondaryButtonY));
                SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, TertiaryButtonY));
                return;
            }

            SetPosition(shutdownButton.GetComponent<RectTransform>(), new Vector2(0f, StartButtonY));
            SetPosition(quitButton.GetComponent<RectTransform>(), new Vector2(0f, SecondaryButtonY));
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("Friend Slop UI");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            canvasObject.AddComponent<GraphicRaycaster>();

            var hudRoot = CreatePanel("HUD", canvasObject.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(760f, 180f), new Color(0f, 0f, 0f, 0f));
            quotaText = CreateText("TeamMoney", canvasObject.transform, "Team Money: $0", 22, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(380f, 36f));
            timerText = CreateText("Timer", hudRoot.transform, "Parts: Cockpit missing | Wings missing | Engine missing", 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(760f, 32f));
            carriedText = CreateText("Carried", hudRoot.transform, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -38f), new Vector2(620f, 30f));
            resultText = CreateText("Result", hudRoot.transform, string.Empty, 22, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -78f), new Vector2(720f, 42f));

            promptText = CreateText("Prompt", canvasObject.transform, string.Empty, 24, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(760f, 42f));
            CreateText("Reticle", canvasObject.transform, "+", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));

            menuRoot = CreatePanel("Menu", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MenuWidth, DisconnectedMenuHeight), new Color(0.04f, 0.05f, 0.05f, 0.96f));
            menuRect = menuRoot.GetComponent<RectTransform>();
            titleText = CreateText("Title", menuRoot.transform, "FRIEND SLOP RETRIEVAL", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(460f, 34f));
            statusText = CreateText("Status", menuRoot.transform, "Not connected.", 14, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(440f, 72f));
            lobbyQueueText = CreateText("LobbyQueue", menuRoot.transform, string.Empty, 15, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -176f), new Vector2(440f, 82f));

            joinInput = CreateInput("JoinInput", menuRoot.transform, "Relay code or LAN IP", new Vector2(0f, 68f));
            hostButton = CreateButton("Host Online", menuRoot.transform, new Vector2(0f, HostButtonY), () => NetworkSessionManager.Instance?.HostOnline());
            joinButton = CreateButton("Join Code", menuRoot.transform, new Vector2(0f, JoinButtonY), () => NetworkSessionManager.Instance?.JoinOnline(joinInput.text));
            localHostButton = CreateButton("Host LAN", menuRoot.transform, new Vector2(0f, LocalHostButtonY), () => NetworkSessionManager.Instance?.StartLocalHost());
            localJoinButton = CreateButton("Join LAN", menuRoot.transform, new Vector2(0f, LocalJoinButtonY), () => NetworkSessionManager.Instance?.StartLocalClient(string.IsNullOrWhiteSpace(joinInput.text) ? "127.0.0.1" : joinInput.text));
            startButton = CreateButton("Start Round", menuRoot.transform, new Vector2(0f, StartButtonY), () =>
            {
                RoundManager.Instance?.RequestStartRoundServerRpc();
                LockGameplayCursor();
            });
            restartButton = CreateButton("Restart Round", menuRoot.transform, new Vector2(0f, StartButtonY), () =>
            {
                RoundManager.Instance?.RequestRestartRoundServerRpc();
                LockGameplayCursor();
            });
            shutdownButton = CreateButton("Leave Session", menuRoot.transform, new Vector2(0f, SecondaryButtonY), () => NetworkSessionManager.Instance?.Shutdown());
            quitButton = CreateButton("Quit", menuRoot.transform, new Vector2(0f, TertiaryButtonY), QuitGame);
        }

        private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
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
            rect.pivot = new Vector2(0.5f, 0.5f);
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

        private static void SetPosition(RectTransform rectTransform, Vector2 anchoredPosition)
        {
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = anchoredPosition;
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
    }
}
