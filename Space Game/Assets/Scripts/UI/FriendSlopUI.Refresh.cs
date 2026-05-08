using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.UI
{
    // Dirty/timed refresh coordinator plus the lightweight per-frame HUD path.
    public partial class FriendSlopUI
    {
        private void RequestUiRefresh()
        {
            _uiRefreshRequested = true;
        }

        private void ReleaseUiRefreshSubscriptions()
        {
            if (_observedUiRound != null)
                UnsubscribeFromRoundRefresh(_observedUiRound);

            _observedUiRound = null;
            _hasUiRefreshSnapshot = false;
        }

        private void ObserveRoundForUiRefresh()
        {
            var current = RoundManagerRegistry.Current;
            if (ReferenceEquals(current, _observedUiRound)) return;

            if (_observedUiRound != null)
                UnsubscribeFromRoundRefresh(_observedUiRound);

            _observedUiRound = current;

            if (_observedUiRound != null)
                SubscribeToRoundRefresh(_observedUiRound);

            RequestUiRefresh();
        }

        private void SubscribeToRoundRefresh(RoundManager round)
        {
            round.Phase.OnValueChanged += HandleRoundPhaseRefreshChanged;
            round.CollectedValue.OnValueChanged += HandleRoundIntRefreshChanged;
            round.Quota.OnValueChanged += HandleRoundIntRefreshChanged;
            round.HasCockpit.OnValueChanged += HandleRoundBoolRefreshChanged;
            round.HasWings.OnValueChanged += HandleRoundBoolRefreshChanged;
            round.HasEngine.OnValueChanged += HandleRoundBoolRefreshChanged;
            round.RocketAssembled.OnValueChanged += HandleRoundBoolRefreshChanged;
            round.PlayersBoarded.OnValueChanged += HandleRoundIntRefreshChanged;
            round.PlayersReady.OnValueChanged += HandleRoundIntRefreshChanged;
            round.PlayersExpectedToLoad.OnValueChanged += HandleRoundIntRefreshChanged;
            round.CurrentTier.OnValueChanged += HandleRoundIntRefreshChanged;
            round.CurrentPlanetCatalogIndex.OnValueChanged += HandleRoundIntRefreshChanged;
            round.SelectedNextPlanetCatalogIndex.OnValueChanged += HandleRoundIntRefreshChanged;
            round.ExpeditionsCompleted.OnValueChanged += HandleRoundIntRefreshChanged;
            round.IsExtractionWindow.OnValueChanged += HandleRoundBoolRefreshChanged;
            if (round.NextPlanetChoiceIndices != null)
                round.NextPlanetChoiceIndices.OnListChanged += HandleNextPlanetChoicesRefreshChanged;
        }

        private void UnsubscribeFromRoundRefresh(RoundManager round)
        {
            round.Phase.OnValueChanged -= HandleRoundPhaseRefreshChanged;
            round.CollectedValue.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.Quota.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.HasCockpit.OnValueChanged -= HandleRoundBoolRefreshChanged;
            round.HasWings.OnValueChanged -= HandleRoundBoolRefreshChanged;
            round.HasEngine.OnValueChanged -= HandleRoundBoolRefreshChanged;
            round.RocketAssembled.OnValueChanged -= HandleRoundBoolRefreshChanged;
            round.PlayersBoarded.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.PlayersReady.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.PlayersExpectedToLoad.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.CurrentTier.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.CurrentPlanetCatalogIndex.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.SelectedNextPlanetCatalogIndex.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.ExpeditionsCompleted.OnValueChanged -= HandleRoundIntRefreshChanged;
            round.IsExtractionWindow.OnValueChanged -= HandleRoundBoolRefreshChanged;
            if (round.NextPlanetChoiceIndices != null)
                round.NextPlanetChoiceIndices.OnListChanged -= HandleNextPlanetChoicesRefreshChanged;
        }

        private void HandleRoundPhaseRefreshChanged(RoundPhase previous, RoundPhase current) => RequestUiRefresh();
        private void HandleRoundIntRefreshChanged(int previous, int current) => RequestUiRefresh();
        private void HandleRoundBoolRefreshChanged(bool previous, bool current) => RequestUiRefresh();
        private void HandleNextPlanetChoicesRefreshChanged(NetworkListEvent<int> changeEvent) => RequestUiRefresh();

        private void RefreshUiIfNeeded()
        {
            ObserveRoundForUiRefresh();

            if (ShouldRunFullUiRefresh())
            {
                _uiRefreshRequested = false;
                RefreshUi();
                CaptureUiRefreshSnapshot();
                return;
            }

            RefreshFrameUi();
        }

        private bool ShouldRunFullUiRefresh()
        {
            if (_uiRefreshRequested || HasUiRefreshStateChanged())
                return true;

            if (!NeedsTimedFullUiRefresh())
                return false;

            if (Time.unscaledTime < _nextTimedUiRefreshTime)
                return false;

            _nextTimedUiRefreshTime = Time.unscaledTime + FullUiRefreshInterval;
            return true;
        }

        private bool HasUiRefreshStateChanged()
        {
            var networkManager = NetworkManager.Singleton;
            var session = SessionManager;
            var round = RoundManagerRegistry.Current;
            var connected = networkManager != null && networkManager.IsListening;
            var isHost = networkManager != null && networkManager.IsHost;
            var clientCount = connected ? networkManager.ConnectedClientsIds.Count : 0;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var canCancelSessionOperation = session != null && session.CanCancelSessionOperation;
            var connectionInProgress = session != null && session.IsSessionOperationInProgress;

            return !_hasUiRefreshSnapshot ||
                   connected != _lastRefreshConnected ||
                   isHost != _lastRefreshIsHost ||
                   canCancelSessionOperation != _lastRefreshCanCancelSessionOperation ||
                   connectionInProgress != _lastRefreshConnectionInProgress ||
                   activeMenuOpen != _lastRefreshActiveMenuOpen ||
                   _lateJoinLoading != _lastRefreshLateJoinLoading ||
                   clientCount != _lastRefreshConnectedClientCount ||
                   phase != _lastRefreshPhase;
        }

        private void CaptureUiRefreshSnapshot()
        {
            var networkManager = NetworkManager.Singleton;
            var session = SessionManager;
            var round = RoundManagerRegistry.Current;
            var connected = networkManager != null && networkManager.IsListening;

            _hasUiRefreshSnapshot = true;
            _lastRefreshConnected = connected;
            _lastRefreshIsHost = networkManager != null && networkManager.IsHost;
            _lastRefreshCanCancelSessionOperation = session != null && session.CanCancelSessionOperation;
            _lastRefreshConnectionInProgress = session != null && session.IsSessionOperationInProgress;
            _lastRefreshActiveMenuOpen = activeMenuOpen;
            _lastRefreshLateJoinLoading = _lateJoinLoading;
            _lastRefreshConnectedClientCount = connected ? networkManager.ConnectedClientsIds.Count : 0;
            _lastRefreshPhase = round != null ? round.Phase.Value : RoundPhase.Lobby;
        }

        private bool NeedsTimedFullUiRefresh()
        {
            var networkManager = NetworkManager.Singleton;
            var session = SessionManager;
            var round = RoundManagerRegistry.Current;
            var connected = networkManager != null && networkManager.IsListening;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var isLoading = phase == RoundPhase.Loading || phase == RoundPhase.Transitioning || _lateJoinLoading;
            var gameplayPhase = connected && RoundStateUtility.AllowsGameplayInput(phase);
            var menuVisible = !isLoading && (!gameplayPhase || activeMenuOpen);

            return menuVisible ||
                   (session != null && (session.CanCancelSessionOperation || session.IsSessionOperationInProgress)) ||
                   Time.unscaledTime < _copyCodeFeedbackUntil;
        }

        private void RefreshFrameUi()
        {
            var networkManager = NetworkManager.Singleton;
            var round = RoundManagerRegistry.Current;
            var connected = networkManager != null && networkManager.IsListening;
            var phase = round != null ? round.Phase.Value : RoundPhase.Lobby;
            var activeRound = connected && phase == RoundPhase.Active;
            var gameplayPhase = connected && RoundStateUtility.AllowsGameplayInput(phase);
            var isLoading = phase == RoundPhase.Loading || phase == RoundPhase.Transitioning || _lateJoinLoading;

            UpdateLoadingScreen(isLoading, phase, round);
            if (moneyPanelRect != null)
                moneyPanelRect.gameObject.SetActive(activeRound);
            if (timerText != null)
                timerText.gameObject.SetActive(activeRound);
            LayoutHud();
            RefreshHudText(round);
            RefreshPlayerHud(connected, phase, gameplayPhase, activeRound);
        }

        private void RefreshHudText(RoundManager round)
        {
            if (quotaText != null)
                quotaText.text = round != null ? $"Team Money: ${round.CollectedValue.Value}" : "Team Money: $0";
            if (timerText != null)
                timerText.text = BuildObjectiveHudText(round);
        }

        private void RefreshPlayerHud(bool connected, RoundPhase phase, bool gameplayPhase, bool activeRound)
        {
            var localPlayer = LocalPlayerRegistry.Current;
            if (promptText != null)
            {
                promptText.text = localPlayer != null && localPlayer.Interactor != null
                    ? localPlayer.Interactor.CurrentPrompt
                    : string.Empty;
            }

            UpdateStaminaBar(localPlayer, gameplayPhase);
            UpdateHealthBar(localPlayer, gameplayPhase);
            UpdateDeathOverlay(localPlayer, activeRound);
            UpdateChargeBar(localPlayer, gameplayPhase);
            UpdateInventoryHud(localPlayer, activeRound);
            UpdateChatPanel(connected);
            UpdateCompass(localPlayer, phase);
        }
    }
}
