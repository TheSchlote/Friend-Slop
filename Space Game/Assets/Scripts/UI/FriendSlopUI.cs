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
        public static FriendSlopUI Instance { get; private set; }
        public static bool BlocksGameplayInput => Instance != null && Instance.IsBlockingGameplayInput();

        private GameObject menuRoot;
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
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var activeRound = connected && phase == RoundPhase.Active;
            var showMenu = !activeRound || menuPinned || Cursor.lockState != CursorLockMode.Locked;

            menuRoot.SetActive(showMenu);

            if (session != null)
            {
                var code = string.IsNullOrWhiteSpace(session.LastJoinCode) ? string.Empty : $"\nJoin: {session.LastJoinCode}";
                statusText.text = session.Status + code + "\nTab toggles this menu. Esc unlocks mouse.";
            }

            lobbyQueueText.text = BuildLobbyQueueText(networkManager);
            hostButton.gameObject.SetActive(!connected);
            joinButton.gameObject.SetActive(!connected);
            localHostButton.gameObject.SetActive(!connected);
            localJoinButton.gameObject.SetActive(!connected);
            joinInput.gameObject.SetActive(!connected);

            var isHost = networkManager != null && networkManager.IsHost;
            var showStart = connected && isHost && phase == RoundPhase.Lobby;
            var showRestart = connected && isHost && (phase == RoundPhase.Success || phase == RoundPhase.Failed);
            startButton.gameObject.SetActive(showStart);
            restartButton.gameObject.SetActive(showRestart);
            shutdownButton.gameObject.SetActive(connected);
            ApplyMenuLayout(connected, showStart, showRestart);

            if (round != null)
            {
                quotaText.text = $"Money: ${round.CollectedValue.Value}";
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

        private void BuildUi()
        {
            var canvasObject = new GameObject("Friend Slop UI");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var hudRoot = CreatePanel("HUD", canvasObject.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(420f, 180f), new Color(0f, 0f, 0f, 0f));
            quotaText = CreateText("Quota", hudRoot.transform, "Quota: $0 / $0", 22, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(420f, 32f));
            timerText = CreateText("Timer", hudRoot.transform, "Timer: 00:00", 22, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -34f), new Vector2(420f, 32f));
            carriedText = CreateText("Carried", hudRoot.transform, string.Empty, 19, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -70f), new Vector2(520f, 30f));
            resultText = CreateText("Result", hudRoot.transform, string.Empty, 22, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -112f), new Vector2(620f, 42f));

            promptText = CreateText("Prompt", canvasObject.transform, string.Empty, 24, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 72f), new Vector2(760f, 42f));
            CreateText("Reticle", canvasObject.transform, "+", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));

            menuRoot = CreatePanel("Menu", canvasObject.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430f, 640f), new Color(0.04f, 0.05f, 0.05f, 0.88f));
            CreateText("Title", menuRoot.transform, "FRIEND SLOP RETRIEVAL", 27, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(390f, 46f));
            statusText = CreateText("Status", menuRoot.transform, "Not connected.", 17, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(380f, 70f));
            lobbyQueueText = CreateText("Lobby Queue", menuRoot.transform, "Lobby Queue\nNo one connected.", 17, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -154f), new Vector2(310f, 90f));

            joinInput = CreateInput("JoinInput", menuRoot.transform, "Join code or LAN IP", new Vector2(0f, 8f));
            hostButton = CreateButton("Host Online", menuRoot.transform, new Vector2(0f, -46f), () => NetworkSessionManager.Instance?.HostOnline());
            joinButton = CreateButton("Join Code", menuRoot.transform, new Vector2(0f, -100f), () => NetworkSessionManager.Instance?.JoinOnline(joinInput.text));
            localHostButton = CreateButton("Host LAN", menuRoot.transform, new Vector2(0f, -154f), () => NetworkSessionManager.Instance?.StartLocalHost());
            localJoinButton = CreateButton("Join LAN", menuRoot.transform, new Vector2(0f, -208f), () => NetworkSessionManager.Instance?.StartLocalClient(string.IsNullOrWhiteSpace(joinInput.text) ? "127.0.0.1" : joinInput.text));
            startButton = CreateButton("Start Round", menuRoot.transform, new Vector2(0f, -262f), () =>
            {
                RoundManager.Instance?.RequestStartRoundRpc();
                LockGameplayCursor();
            });
            restartButton = CreateButton("Restart Round", menuRoot.transform, new Vector2(0f, -262f), () =>
            {
                RoundManager.Instance?.RequestRestartRoundRpc();
                LockGameplayCursor();
            });
            shutdownButton = CreateButton("Leave Session", menuRoot.transform, new Vector2(0f, -316f), () => NetworkSessionManager.Instance?.Shutdown());
            quitButton = CreateButton("Quit", menuRoot.transform, new Vector2(0f, -370f), QuitGame);
        }

        private void ApplyMenuLayout(bool connected, bool showStart, bool showRestart)
        {
            if (!connected)
            {
                SetAnchoredY(joinInput.gameObject, -10f);
                SetAnchoredY(hostButton.gameObject, -64f);
                SetAnchoredY(joinButton.gameObject, -118f);
                SetAnchoredY(localHostButton.gameObject, -172f);
                SetAnchoredY(localJoinButton.gameObject, -226f);
                SetAnchoredY(quitButton.gameObject, -300f);
                return;
            }

            var y = -24f;
            if (showStart)
            {
                SetAnchoredY(startButton.gameObject, y);
                y -= 54f;
            }
            else if (showRestart)
            {
                SetAnchoredY(restartButton.gameObject, y);
                y -= 54f;
            }

            SetAnchoredY(shutdownButton.gameObject, y);
            SetAnchoredY(quitButton.gameObject, y - 54f);
        }

        private static void SetAnchoredY(GameObject gameObject, float y)
        {
            if (gameObject == null || !gameObject.TryGetComponent<RectTransform>(out var rect))
            {
                return;
            }

            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
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
            var buttonObject = CreatePanel(label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(250f, 42f), new Color(0.16f, 0.18f, 0.18f, 0.96f));
            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(onClick);
            CreateText(label + " Text", buttonObject.transform, label, 18, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return button;
        }

        private static string FormatPart(bool installed, string label)
        {
            return installed ? $"{label} OK" : $"{label} missing";
        }

        private static string BuildLobbyQueueText(NetworkManager networkManager)
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return "Lobby Queue\nNo one connected.";
            }

            var builder = new StringBuilder("Lobby Queue");
            var clients = networkManager.ConnectedClientsList;
            for (var i = 0; i < clients.Count; i++)
            {
                var clientId = clients[i].ClientId;
                var role = clientId == NetworkManager.ServerClientId ? "Host" : $"Player {i + 1}";
                var localMarker = clientId == networkManager.LocalClientId ? " (you)" : string.Empty;
                builder.Append('\n').Append(i + 1).Append(". ").Append(role).Append(localMarker);
            }

            return builder.ToString();
        }

        private InputField CreateInput(string name, Transform parent, string placeholder, Vector2 anchoredPosition)
        {
            var inputObject = CreatePanel(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(250f, 42f), new Color(0.02f, 0.02f, 0.02f, 0.95f));
            var input = inputObject.AddComponent<InputField>();
            var text = CreateText("Text", inputObject.transform, string.Empty, 18, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-20f, 0f));
            var placeholderText = CreateText("Placeholder", inputObject.transform, placeholder, 16, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-20f, 0f));
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

        private static void QuitGame()
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
