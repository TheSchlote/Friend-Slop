using System.Collections.Generic;
using System.Text;
using FriendSlop.Player;
using FriendSlop.Round;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // Runtime chat panel, hotkeys, and message rendering.
    public partial class FriendSlopUI
    {
        private RectTransform chatPanelRect;
        private Image chatPanelBackdrop;
        private Text chatLogText;
        private InputField chatInput;
        private bool _chatInputFocused;
        private int _chatLastClosedFrame = -1;
        private readonly List<ChatEntry> _chatMessages = new();
        private const int MaxChatMessages = 8;
        private const float ChatMessageVisibleSeconds = 10f;
        private const float ChatMessageFadeSeconds = 2f;
        private static readonly Color ChatSenderColor = new(0.95f, 0.78f, 0.18f, 1f);
        private static readonly Color ChatMessageColor = new(0.96f, 0.96f, 0.96f, 1f);

        private struct ChatEntry
        {
            public string Sender;
            public string Message;
            public float ReceivedTime;
        }

        private void BuildChatPanel(GameObject canvasObject)
        {
            const float panelWidth = 440f;
            const float panelHeight = 220f;
            const float inputHeight = 30f;

            var panel = CreatePanel("ChatPanel", canvasObject.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(18f, 110f), new Vector2(panelWidth, panelHeight),
                new Color(0f, 0f, 0f, 0f));
            chatPanelRect = panel.GetComponent<RectTransform>();

            var backdrop = CreatePanel("ChatBackdrop", panel.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0f, 0f, 0f, 0.55f));
            chatPanelBackdrop = backdrop.GetComponent<Image>();
            chatPanelBackdrop.raycastTarget = false;
            backdrop.SetActive(false);

            chatLogText = CreateText("ChatLog", panel.transform, string.Empty, 14, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, inputHeight * 0.5f + 2f), new Vector2(-12f, -inputHeight - 4f));
            chatLogText.supportRichText = true;
            chatLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
            chatLogText.verticalOverflow = VerticalWrapMode.Truncate;
            chatLogText.raycastTarget = false;
            var logOutline = chatLogText.gameObject.AddComponent<Outline>();
            logOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            logOutline.effectDistance = new Vector2(1f, -1f);

            var inputBg = CreatePanel("ChatInputBg", panel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, inputHeight * 0.5f), new Vector2(-12f, inputHeight),
                new Color(0.05f, 0.06f, 0.08f, 0.92f));
            chatInput = inputBg.AddComponent<InputField>();
            chatInput.lineType = InputField.LineType.SingleLine;
            chatInput.characterLimit = NetworkFirstPersonController.MaxChatMessageLength;
            var chatInputText = CreateText("Text", inputBg.transform, string.Empty, 14, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-20f, 0f));
            var chatInputPlaceholder = CreateText("Placeholder", inputBg.transform,
                "Press Enter to chat...", 13, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-20f, 0f));
            chatInputPlaceholder.color = new Color(1f, 1f, 1f, 0.42f);
            chatInput.textComponent = chatInputText;
            chatInput.placeholder = chatInputPlaceholder;
            chatInput.onEndEdit.AddListener(OnChatInputEndEdit);
            inputBg.SetActive(false);
        }

        private void OnChatMessageReceived(string sender, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            _chatMessages.Add(new ChatEntry
            {
                Sender = sender ?? "Player",
                Message = message,
                ReceivedTime = Time.time,
            });

            while (_chatMessages.Count > MaxChatMessages)
                _chatMessages.RemoveAt(0);
        }

        private void HandleChatHotkeys(bool connected)
        {
            if (chatInput == null) return;
            if (!connected)
            {
                if (_chatInputFocused) CloseChatInput(send: false);
                return;
            }
            if (Keyboard.current == null) return;

            if (_chatInputFocused)
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame || activeMenuOpen)
                    CloseChatInput(send: false);
                return;
            }

            if (!Keyboard.current.enterKey.wasPressedThisFrame) return;
            if (Time.frameCount == _chatLastClosedFrame) return;
            if (activeMenuOpen) return;
            if (playerNameInput != null && playerNameInput.isFocused) return;
            if (joinInput != null && joinInput.isFocused) return;

            OpenChatInput();
        }

        private void OpenChatInput()
        {
            if (chatInput == null) return;
            _chatInputFocused = true;
            chatInput.gameObject.SetActive(true);
            chatInput.text = string.Empty;
            chatInput.ActivateInputField();
            chatInput.Select();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CloseChatInput(bool send)
        {
            if (chatInput == null) return;
            _chatInputFocused = false;
            _chatLastClosedFrame = Time.frameCount;
            if (!send) chatInput.text = string.Empty;
            chatInput.DeactivateInputField();
            chatInput.gameObject.SetActive(false);

            var round = RoundManager.Instance;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            if (!activeMenuOpen && RoundStateUtility.AllowsGameplayInput(phase))
                LockGameplayCursor();
        }

        private void OnChatInputEndEdit(string value)
        {
            if (!_chatInputFocused) return;
            var trimmed = (value ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                var local = NetworkFirstPersonController.LocalPlayer;
                if (local != null) local.SendChatMessageServerRpc(trimmed);
            }
            CloseChatInput(send: true);
        }

        private void UpdateChatPanel(bool connected)
        {
            if (chatPanelRect == null) return;
            chatPanelRect.gameObject.SetActive(connected);
            if (!connected)
            {
                if (_chatMessages.Count > 0) _chatMessages.Clear();
                if (chatPanelBackdrop != null) chatPanelBackdrop.gameObject.SetActive(false);
                return;
            }

            if (chatPanelBackdrop != null)
                chatPanelBackdrop.gameObject.SetActive(_chatInputFocused);

            if (chatLogText == null) return;

            var now = Time.time;
            var sb = new StringBuilder();
            for (var i = 0; i < _chatMessages.Count; i++)
            {
                var entry = _chatMessages[i];
                float alpha;
                if (_chatInputFocused)
                {
                    alpha = 1f;
                }
                else
                {
                    var age = now - entry.ReceivedTime;
                    if (age <= ChatMessageVisibleSeconds) alpha = 1f;
                    else if (age < ChatMessageVisibleSeconds + ChatMessageFadeSeconds)
                        alpha = 1f - (age - ChatMessageVisibleSeconds) / ChatMessageFadeSeconds;
                    else continue;
                }

                if (sb.Length > 0) sb.Append('\n');
                AppendChatLine(sb, entry.Sender, entry.Message, alpha);
            }
            chatLogText.text = sb.ToString();
        }

        private static void AppendChatLine(StringBuilder sb, string sender, string message, float alpha)
        {
            var senderColor = ChatSenderColor;
            senderColor.a = alpha;
            var msgColor = ChatMessageColor;
            msgColor.a = alpha;
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(senderColor)).Append('>')
              .Append(sender).Append(":</color> ");
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(msgColor)).Append('>')
              .Append(message).Append("</color>");
        }
    }
}
