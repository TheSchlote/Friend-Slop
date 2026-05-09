using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public partial class RoundManager
    {
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSelectNextPlanetServerRpc(int catalogIndex, RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;

            var planet = GetCatalogPlanet(catalogIndex);
            if (planet == null) return;
            if (planet.Tier != NextTier) return;
            if (!IsOfferedNextPlanetIndex(catalogIndex)) return;
            SelectedNextPlanetCatalogIndex.Value = catalogIndex;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestTravelToNextPlanetServerRpc(RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;
            if (Phase.Value != RoundPhase.Success) return;

            ServerAdvanceToNextPlanet();
        }

        // Test-mode entry point: jump to any catalog planet regardless of tier or
        // progression state. Reuses ServerTransitionToPlanet so scene loading, cleanup,
        // and the loading screen all match the normal travel flow.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartTestRoundServerRpc(int catalogIndex, RpcParams rpcParams = default)
        {
            if (NetworkManager != null && rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
                return;
            if (Phase.Value == RoundPhase.Active
                || Phase.Value == RoundPhase.Loading
                || Phase.Value == RoundPhase.Transitioning) return;

            var planet = GetCatalogPlanet(catalogIndex);
            if (planet == null || planetCatalog == null) return;

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(ServerTransitionToPlanet(planet, catalogIndex));
        }

        public PlanetCatalog Catalog => planetCatalog;
        public int FinalTier => planetCatalog != null ? planetCatalog.HighestAuthoredTier : PlanetCatalog.MaxTier;
        public int NextTier => Mathf.Min(CurrentTier.Value + 1, FinalTier);
        public bool HasReachedFinalTier => CurrentTier.Value >= FinalTier;
        public PlanetDefinition CurrentPlanet => GetCatalogPlanet(CurrentPlanetCatalogIndex.Value);
        public PlanetDefinition SelectedNextPlanet => GetCatalogPlanet(SelectedNextPlanetCatalogIndex.Value);
        public RoundObjective ActiveObjective
        {
            get
            {
                var planet = CurrentPlanet;
                return planet != null && planet.Objective != null ? planet.Objective : defaultObjective;
            }
        }
        public bool HasActiveTimer => roundLengthSeconds > 0f;

        public void ServerSetTimer(float seconds)
        {
            if (!IsServer) return;
            roundLengthSeconds = Mathf.Max(0f, seconds);
            TimeRemaining.Value = roundLengthSeconds;
        }

        // Called by survival-style objectives when the survival timer expires and a grace
        // window should run before final resolution. Idempotent for Evaluate polling.
        public void ServerOpenExtractionGrace(float seconds)
        {
            if (!IsServer) return;
            if (IsExtractionWindow.Value) return;
            IsExtractionWindow.Value = true;
            ServerSetTimer(Mathf.Max(0.1f, seconds));
        }

        public List<PlanetDefinition> GetNextTierCandidates()
        {
            return planetCatalog != null
                ? planetCatalog.GetPlanetsForTier(NextTier)
                : new List<PlanetDefinition>();
        }

        // Returns the random subset offered to the host on the current Success screen.
        // Before any offer is rolled, return the whole tier so menus stay populated.
        public List<PlanetDefinition> GetOfferedNextPlanetChoices()
        {
            var result = new List<PlanetDefinition>();
            if (NextPlanetChoiceIndices != null && NextPlanetChoiceIndices.Count > 0)
            {
                for (var i = 0; i < NextPlanetChoiceIndices.Count; i++)
                {
                    var planet = GetCatalogPlanet(NextPlanetChoiceIndices[i]);
                    if (planet != null && planet.Tier == NextTier) result.Add(planet);
                }
            }
            if (result.Count == 0) return GetNextTierCandidates();
            return result;
        }

        private bool IsOfferedNextPlanetIndex(int catalogIndex)
        {
            if (NextPlanetChoiceIndices == null || NextPlanetChoiceIndices.Count == 0) return true;
            for (var i = 0; i < NextPlanetChoiceIndices.Count; i++)
                if (NextPlanetChoiceIndices[i] == catalogIndex) return true;
            return false;
        }

        private void ServerRollNextPlanetChoices()
        {
            if (!IsServer || NextPlanetChoiceIndices == null) return;
            NextPlanetChoiceIndices.Clear();
            if (planetCatalog == null || HasReachedFinalTier) return;

            var pool = GetNextTierCandidates();
            if (pool.Count == 0) return;

            if (pool.Count <= MaxNextPlanetChoices)
            {
                for (var i = 0; i < pool.Count; i++)
                {
                    var idx = planetCatalog.IndexOf(pool[i]);
                    if (idx >= 0) NextPlanetChoiceIndices.Add(idx);
                }
                return;
            }

            for (var i = 0; i < MaxNextPlanetChoices && pool.Count > 0; i++)
            {
                var pickIndex = Random.Range(0, pool.Count);
                var planet = pool[pickIndex];
                pool.RemoveAt(pickIndex);
                var idx = planetCatalog.IndexOf(planet);
                if (idx >= 0) NextPlanetChoiceIndices.Add(idx);
            }
        }

        private PlanetDefinition GetCatalogPlanet(int index)
        {
            return planetCatalog != null ? planetCatalog.GetByIndex(index) : null;
        }

        private void ServerAdvanceToNextPlanet()
        {
            if (!IsServer) return;
            if (HasReachedFinalTier)
            {
                ServerReturnToExpeditionLobby();
                return;
            }

            var next = SelectedNextPlanet;
            if (next == null || next.Tier != NextTier)
            {
                var offered = GetOfferedNextPlanetChoices();
                next = offered.Count > 0 ? offered[0] : null;
            }

            if (next == null)
            {
                SelectedNextPlanetCatalogIndex.Value = -1;
                ServerStartRound();
                return;
            }

            var nextIndex = planetCatalog != null ? planetCatalog.IndexOf(next) : -1;
            SelectedNextPlanetCatalogIndex.Value = nextIndex;

            if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
            _transitionCoroutine = StartCoroutine(ServerTransitionToPlanet(next, nextIndex));
        }

        private IEnumerator ServerTransitionToPlanet(PlanetDefinition next, int nextIndex)
        {
            ServerSetPhase(RoundPhase.Transitioning);
            yield return new WaitForSeconds(TransitionFadeSeconds);

            var previousTier = CurrentTier.Value;
            var previousPlanetCatalogIndex = CurrentPlanetCatalogIndex.Value;
            var previousQuota = quota;
            var previousRoundLengthSeconds = roundLengthSeconds;

            ServerCleanupRoundActorsForPlanetTravel();
            CurrentTier.Value = next.Tier;
            CurrentPlanetCatalogIndex.Value = nextIndex;
            ApplyPlanetOverrides(next);

            yield return new WaitForSeconds(TransitionHoldSeconds);

            const float maxEnvWaitSeconds = PlanetSceneOrchestrator.SceneEventRetrySeconds + 8f;
            var envWaitStart = Time.time;
            while (PlanetSceneOwnership.FindRoundReadyEnvironment(next) == null
                   && Time.time - envWaitStart < maxEnvWaitSeconds)
            {
                yield return null;
            }

            if (PlanetSceneOwnership.FindRoundReadyEnvironment(next) == null)
            {
                Debug.LogError(
                    $"RoundManager: planet '{next.name}' was not round-ready after {maxEnvWaitSeconds:0.#}s " +
                    $"({PlanetSceneOwnership.GetReadinessProblem(next)}). Restoring the previous planet instead of starting a broken round.");

                quota = previousQuota;
                roundLengthSeconds = previousRoundLengthSeconds;
                Quota.Value = quota;
                TimeRemaining.Value = roundLengthSeconds;
                CurrentTier.Value = previousTier;
                CurrentPlanetCatalogIndex.Value = previousPlanetCatalogIndex;
                ApplyActivePlanetEnvironment();

                SelectedNextPlanetCatalogIndex.Value = -1;
                _transitionCoroutine = null;
                ServerSetPhase(RoundPhase.Success);
                yield break;
            }

            ApplyActivePlanetEnvironment();
            SelectedNextPlanetCatalogIndex.Value = -1;
            _transitionCoroutine = null;
            ServerStartRound();
        }

        private void ApplyPlanetOverrides(PlanetDefinition planet)
        {
            if (planet == null) return;
            quota = planet.QuotaOverride > 0 ? planet.QuotaOverride : baseQuota;
            roundLengthSeconds = planet.RoundLengthOverride > 0f ? planet.RoundLengthOverride : baseRoundLengthSeconds;
        }

        private void ServerReturnToExpeditionLobby()
        {
            if (!IsServer) return;

            var firstPlanet = planetCatalog != null
                ? planetCatalog.GetFirstForTier(1)
                : startingPlanet;
            if (firstPlanet != null)
            {
                CurrentTier.Value = firstPlanet.Tier;
                CurrentPlanetCatalogIndex.Value = planetCatalog != null ? planetCatalog.IndexOf(firstPlanet) : -1;
                ApplyPlanetOverrides(firstPlanet);
            }

            CollectedValue.Value = 0;
            TimeRemaining.Value = roundLengthSeconds;
            Quota.Value = quota;
            HasCockpit.Value = false;
            HasWings.Value = false;
            HasEngine.Value = false;
            RocketAssembled.Value = false;
            IsExtractionWindow.Value = false;
            finalTierSuccessRecorded = false;
            boardedPlayerIds.Clear();
            _readyPlayerIds.Clear();
            PlayersBoarded.Value = 0;
            PlayersReady.Value = 0;
            PlayersExpectedToLoad.Value = 0;
            SelectedNextPlanetCatalogIndex.Value = -1;
            if (NextPlanetChoiceIndices != null)
                NextPlanetChoiceIndices.Clear();

            ServerSetPhase(RoundPhase.Lobby);
        }
    }
}
