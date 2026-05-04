using FriendSlop.Networking;
using FriendSlop.Player;
using FriendSlop.Round;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // Canvas construction, widget factories, and shared layout utilities.
    // See FriendSlopUI.cs for fields and lifecycle entry points.
    public partial class FriendSlopUI
    {
        private void BuildInventoryPreviewRig()
        {
            // Lives outside the canvas so it can hold real 3D objects. Parented to this
            // GameObject so it dies with the UI instead of leaking into scene reloads.
            var rigObject = new GameObject("InventoryPreviewRig");
            rigObject.transform.SetParent(transform, worldPositionStays: false);
            inventoryPreviewRig = rigObject.AddComponent<InventoryPreviewRig>();
            inventoryPreviewRig.Initialize(NetworkFirstPersonController.InventorySize);
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

            // Name input panel — top-right corner, visible whenever the menu is showing.
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

            BuildInventoryPanel(canvasObject);
            BuildChatPanel(canvasObject);
            BuildCompass(canvasObject);

            // Game over title — big red text above the menu, only shown on AllDead.
            gameOverText = CreateText("GameOverTitle", canvasObject.transform, "GAME OVER",
                72, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 230f), new Vector2(700f, 90f));
            gameOverText.color = new Color(0.95f, 0.15f, 0.15f, 1f);
            var gameOverOutline = gameOverText.gameObject.AddComponent<Outline>();
            gameOverOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            gameOverOutline.effectDistance = new Vector2(3f, -3f);
            gameOverText.gameObject.SetActive(false);

            // Damage flash overlay — full-screen red, triggered on hit, fades out over ~0.5 s.
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

            // Sun glare — full-screen warm white, fades in when staring at the sun.
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
            hostButton = CreateButton("Host Online", menuRoot.transform, Vector2.zero, () => SessionManager?.HostOnline());
            joinButton = CreateButton("Join Code", menuRoot.transform, Vector2.zero, () => SessionManager?.JoinOnline(joinInput.text));
            localHostButton = CreateButton("Host LAN", menuRoot.transform, Vector2.zero, () => SessionManager?.StartLocalHost());
            localJoinButton = CreateButton("Join LAN", menuRoot.transform, Vector2.zero, () => SessionManager?.StartLocalClient(string.IsNullOrWhiteSpace(joinInput.text) ? "127.0.0.1" : joinInput.text));
            cancelButton = CreateButton("Cancel", menuRoot.transform, Vector2.zero, () => SessionManager?.CancelSessionOperation());
            cancelButton.gameObject.SetActive(false);
            startButton = CreateButton("Start Round", menuRoot.transform, Vector2.zero, () =>
            {
                RoundManagerRegistry.Current?.RequestStartRoundServerRpc();
                LockGameplayCursor();
            });
            restartButton = CreateButton("Restart Round", menuRoot.transform, Vector2.zero, OnRestartOrTravelClicked);
            restartButtonLabel = restartButton.GetComponentInChildren<Text>();
            cyclePlanetButton = CreateButton("Cycle Planet", menuRoot.transform, Vector2.zero, OnCyclePlanetClicked);
            cyclePlanetButtonLabel = cyclePlanetButton.GetComponentInChildren<Text>();
            shutdownButton = CreateButton("Leave Session", menuRoot.transform, Vector2.zero, HandleSessionExitButton);
            quitButton = CreateButton("Quit", menuRoot.transform, Vector2.zero, QuitGame);

            // Loading screen — added last so it renders on top of all other UI.
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

            // Full-screen black overlay for planet transition fades. It sits above menu/HUD,
            // while the loading screen is raised after it so travel status remains visible.
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
            loadingScreenRoot.transform.SetAsLastSibling();
        }

        private void BuildInventoryPanel(GameObject canvasObject)
        {
            const int slotCount = NetworkFirstPersonController.InventorySize;
            const float slotWidth = 120f;
            const float slotHeight = 120f;
            const float slotGap = 10f;
            var totalWidth = slotCount * slotWidth + (slotCount - 1) * slotGap;

            // Anchor at bottom-center, sitting above the prompt line.
            var panel = CreatePanel("InventoryPanel", canvasObject.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 140f), new Vector2(totalWidth, slotHeight),
                new Color(0f, 0f, 0f, 0f));
            inventoryPanelRect = panel.GetComponent<RectTransform>();

            inventorySlotBackgrounds = new Image[slotCount];
            inventorySlotBorders = new Image[slotCount];
            inventorySlotItemTexts = new Text[slotCount];
            inventorySlotValueTexts = new Text[slotCount];
            inventorySlotPreviews = new RawImage[slotCount];

            for (var i = 0; i < slotCount; i++)
            {
                var slotX = -totalWidth * 0.5f + slotWidth * 0.5f + i * (slotWidth + slotGap);

                // Border (sits behind the slot fill so it shows as a thin frame).
                var border = CreatePanel($"Slot{i + 1}Border", panel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(slotX, 0f), new Vector2(slotWidth, slotHeight),
                    InventorySlotBorderIdleColor);
                inventorySlotBorders[i] = border.GetComponent<Image>();

                var slotBg = CreatePanel($"Slot{i + 1}", border.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(slotWidth - 4f, slotHeight - 4f),
                    InventorySlotEmptyColor);
                inventorySlotBackgrounds[i] = slotBg.GetComponent<Image>();

                // 3D preview RawImage. Added first so it sits behind the text overlays.
                var previewObject = new GameObject($"Slot{i + 1}Preview");
                previewObject.transform.SetParent(slotBg.transform, false);
                var previewRect = previewObject.AddComponent<RectTransform>();
                previewRect.anchorMin = new Vector2(0.5f, 0.5f);
                previewRect.anchorMax = new Vector2(0.5f, 0.5f);
                previewRect.pivot = new Vector2(0.5f, 0.5f);
                previewRect.anchoredPosition = new Vector2(0f, 14f);
                previewRect.sizeDelta = new Vector2(72f, 72f);
                var preview = previewObject.AddComponent<RawImage>();
                preview.raycastTarget = false;
                if (inventoryPreviewRig != null)
                {
                    preview.texture = inventoryPreviewRig.RenderTexture;
                    preview.uvRect = inventoryPreviewRig.GetSlotUvRect(i);
                }
                inventorySlotPreviews[i] = preview;

                var numberLabel = CreateText($"Slot{i + 1}Number", slotBg.transform,
                    (i + 1).ToString(), 14, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(8f, -4f), new Vector2(20f, 18f));
                numberLabel.color = new Color(1f, 1f, 1f, 0.85f);
                var numberOutline = numberLabel.gameObject.AddComponent<Outline>();
                numberOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                numberOutline.effectDistance = new Vector2(1f, -1f);

                var itemText = CreateText($"Slot{i + 1}Item", slotBg.transform,
                    string.Empty, 13, TextAnchor.LowerCenter,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 22f), new Vector2(slotWidth - 14f, 14f));
                itemText.horizontalOverflow = HorizontalWrapMode.Overflow;
                var itemOutline = itemText.gameObject.AddComponent<Outline>();
                itemOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                itemOutline.effectDistance = new Vector2(1f, -1f);
                inventorySlotItemTexts[i] = itemText;

                var valueText = CreateText($"Slot{i + 1}Value", slotBg.transform,
                    string.Empty, 12, TextAnchor.LowerCenter,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 4f), new Vector2(slotWidth - 14f, 14f));
                valueText.color = new Color(0.92f, 1f, 0.52f, 1f);
                var valueOutline = valueText.gameObject.AddComponent<Outline>();
                valueOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                valueOutline.effectDistance = new Vector2(1f, -1f);
                inventorySlotValueTexts[i] = valueText;
            }

            // Hidden until the round is active and the local player exists.
            inventoryPanelRect.gameObject.SetActive(false);
        }

        // ── Widget factories ──────────────────────────────────────────────────

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

        // ── Layout utilities (shared by BuildUi and LayoutMenu) ───────────────

        private static Vector2 GetPivotForAnchors(Vector2 anchorMin, Vector2 anchorMax)
        {
            return anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f);
        }

        private static void SetPosition(RectTransform rectTransform, Vector2 anchoredPosition)
        {
            if (rectTransform != null)
                rectTransform.anchoredPosition = anchoredPosition;
        }

        private Vector2 GetCanvasSize()
        {
            if (canvasRect == null)
                return new Vector2(ReferenceWidth, ReferenceHeight);

            var size = canvasRect.rect.size;
            if (size.x <= 0f || size.y <= 0f)
                return new Vector2(ReferenceWidth, ReferenceHeight);

            return size;
        }

        private static void SetButtonSize(Button button, float width, float height)
        {
            if (button == null) return;
            SetSize(button.GetComponent<RectTransform>(), new Vector2(width, height));
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>();
            if (text != null && text.text != label)
                text.text = label;
        }

        private static void SetButtonColor(Button button, Color color)
        {
            if (button == null || button.targetGraphic is not Image image) return;
            image.color = color;
        }

        private static void SetSize(RectTransform rectTransform, Vector2 size)
        {
            if (rectTransform != null)
                rectTransform.sizeDelta = size;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }
    }
}
