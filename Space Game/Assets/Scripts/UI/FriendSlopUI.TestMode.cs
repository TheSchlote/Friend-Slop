using System.Collections.Generic;
using FriendSlop.Round;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    // Lobby-only "Test Mode" affordance for the host. Adds a button below Start Round
    // that opens a planet picker, letting the host jump to any catalog planet on demand
    // without going through the tier progression. Lazily built on first LayoutMenu pass
    // so it can sit entirely outside the main BuildUi flow.
    public partial class FriendSlopUI
    {
        private bool _testModeBuilt;
        private bool _testModePanelOpen;
        private int _testModePopulatedCatalogVersion = -1;

        private Button testModeButton;
        private GameObject testModePanelRoot;
        private RectTransform testModePanelRect;
        private GameObject testModePlanetListContainer;
        private RectTransform testModePlanetListRect;
        private Button testModeBackButton;
        private Text testModeEmptyText;
        private readonly List<Button> _testPlanetButtons = new();

        private const float TestModePanelWidth = 540f;
        private const float TestModePanelHeight = 620f;
        private const float TestModePlanetButtonHeight = 40f;
        private const float TestModePlanetButtonGap = 8f;

        private void EnsureTestModeBuilt()
        {
            if (_testModeBuilt) return;
            if (canvas == null || menuRoot == null) return;
            BuildTestModeUi();
            _testModeBuilt = true;
        }

        private void BuildTestModeUi()
        {
            // Test Mode button lives in the regular menu so it lays out alongside Start Round.
            testModeButton = CreateButton("Test Mode", menuRoot.transform, Vector2.zero, OnTestModeClicked);
            testModeButton.gameObject.SetActive(false);

            // Modal panel parented to the canvas so it can render above the menu when open.
            testModePanelRoot = CreatePanel("TestModePanel", canvas.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(TestModePanelWidth, TestModePanelHeight),
                new Color(0.02f, 0.03f, 0.03f, 0.96f));
            testModePanelRect = testModePanelRoot.GetComponent<RectTransform>();

            // Full-screen backdrop behind the panel intercepts clicks so the menu underneath
            // can't be triggered by accident while the picker is open.
            var backdrop = CreatePanel("TestModeBackdrop", testModePanelRoot.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0f, 0f, 0f, 0.6f));
            // Push the backdrop to the back so the panel content renders on top of it.
            backdrop.transform.SetAsFirstSibling();

            CreateText("TestModeTitle", testModePanelRoot.transform,
                "TEST MODE - PICK ANY PLANET", 22, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -22f), new Vector2(TestModePanelWidth - 32f, 30f));
            CreateText("TestModeSubtitle", testModePanelRoot.transform,
                "Bypasses tier progression. Round ends return to this lobby.",
                14, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -54f), new Vector2(TestModePanelWidth - 48f, 22f))
                .color = new Color(1f, 1f, 1f, 0.65f);

            // Container for the dynamic planet button stack.
            testModePlanetListContainer = CreatePanel("TestModePlanetList", testModePanelRoot.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -86f), new Vector2(TestModePanelWidth - 48f, TestModePanelHeight - 160f),
                new Color(0f, 0f, 0f, 0f));
            testModePlanetListRect = testModePlanetListContainer.GetComponent<RectTransform>();
            testModePlanetListRect.pivot = new Vector2(0.5f, 1f);

            testModeEmptyText = CreateText("TestModeEmpty", testModePlanetListContainer.transform,
                "No planets registered in the catalog yet.", 14, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(TestModePanelWidth - 80f, 22f));
            testModeEmptyText.color = new Color(1f, 0.7f, 0.5f, 0.85f);
            testModeEmptyText.gameObject.SetActive(false);

            testModeBackButton = CreateButton("Back", testModePanelRoot.transform,
                new Vector2(0f, -TestModePanelHeight * 0.5f + 38f), OnTestModeBackClicked);
            SetButtonSize(testModeBackButton, 220f, 40f);

            testModePanelRoot.SetActive(false);
            testModePanelRoot.transform.SetAsLastSibling();
        }

        // Called from LayoutMenu after the standard menu has been laid out. Handles
        // visibility for both the trigger button (lobby-only, host-only) and the modal
        // panel itself, and keeps the planet list synced with the current catalog.
        private void RefreshTestMode(bool connected, bool isHost, RoundPhase phase,
            float buttonWidth, float buttonHeight, float buttonGap, float testButtonY)
        {
            if (!_testModeBuilt) return;

            var canShowButton = connected && isHost && phase == RoundPhase.Lobby;
            if (testModeButton != null)
            {
                testModeButton.gameObject.SetActive(canShowButton);
                if (canShowButton)
                {
                    SetButtonSize(testModeButton, buttonWidth, buttonHeight);
                    SetPosition(testModeButton.GetComponent<RectTransform>(), new Vector2(0f, testButtonY));
                }
            }

            // Auto-close the panel if we leave the lobby (e.g. round just started, host left, etc.)
            if (_testModePanelOpen && !canShowButton)
                _testModePanelOpen = false;

            if (testModePanelRoot != null)
            {
                if (testModePanelRoot.activeSelf != _testModePanelOpen)
                    testModePanelRoot.SetActive(_testModePanelOpen);
                if (_testModePanelOpen)
                {
                    testModePanelRoot.transform.SetAsLastSibling();
                    SyncPlanetButtons();
                }
            }

            // The Test Mode button shouldn't be clickable while the panel is open.
            if (testModeButton != null && _testModePanelOpen && testModeButton.gameObject.activeSelf)
                testModeButton.gameObject.SetActive(false);
        }

        private void SyncPlanetButtons()
        {
            var rm = RoundManagerRegistry.Current;
            var catalog = rm != null ? rm.Catalog : null;

            // Cheap version key: catalog reference + planet count, enough to detect re-imports.
            var version = catalog != null ? catalog.GetInstanceID() + catalog.Count * 31 : 0;
            if (version == _testModePopulatedCatalogVersion && _testPlanetButtons.Count > 0)
                return;
            _testModePopulatedCatalogVersion = version;

            for (var i = 0; i < _testPlanetButtons.Count; i++)
            {
                if (_testPlanetButtons[i] != null)
                    Destroy(_testPlanetButtons[i].gameObject);
            }
            _testPlanetButtons.Clear();

            if (catalog == null || catalog.Count == 0)
            {
                if (testModeEmptyText != null) testModeEmptyText.gameObject.SetActive(true);
                return;
            }

            if (testModeEmptyText != null) testModeEmptyText.gameObject.SetActive(false);

            for (var i = 0; i < catalog.AllPlanets.Count; i++)
            {
                var planet = catalog.AllPlanets[i];
                if (planet == null) continue;
                var label = $"Tier {planet.Tier} - {planet.DisplayName}";
                var capturedIndex = i;
                var button = CreateButton(label, testModePlanetListContainer.transform, Vector2.zero,
                    () => OnTestPlanetClicked(capturedIndex));
                var rect = button.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                var y = -8f - _testPlanetButtons.Count * (TestModePlanetButtonHeight + TestModePlanetButtonGap);
                rect.anchoredPosition = new Vector2(0f, y);
                rect.sizeDelta = new Vector2(TestModePanelWidth - 96f, TestModePlanetButtonHeight);
                _testPlanetButtons.Add(button);
            }
        }

        private void OnTestModeClicked()
        {
            _testModePanelOpen = true;
            _testModePopulatedCatalogVersion = -1; // force a refresh so the list is current
        }

        private void OnTestModeBackClicked()
        {
            _testModePanelOpen = false;
        }

        private void OnTestPlanetClicked(int catalogIndex)
        {
            var rm = RoundManagerRegistry.Current;
            if (rm == null) return;
            rm.RequestStartTestRoundServerRpc(catalogIndex);
            _testModePanelOpen = false;
            LockGameplayCursor();
        }
    }
}
