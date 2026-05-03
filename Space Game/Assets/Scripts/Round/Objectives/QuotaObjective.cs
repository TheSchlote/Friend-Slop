using UnityEngine;

namespace FriendSlop.Round
{
    [CreateAssetMenu(menuName = "Friend Slop/Objectives/Quota", fileName = "QuotaObjective")]
    public class QuotaObjective : RoundObjective
    {
        [SerializeField] private int quotaOverride = 0;
        [SerializeField] private bool requireBoarding = true;
        [SerializeField] private bool failOnTimerExpired = true;

        public override void ServerInitialize(RoundManager round)
        {
            if (round == null) return;
            if (quotaOverride > 0)
                round.Quota.Value = quotaOverride;
        }

        public override ObjectiveStatus Evaluate(RoundManager round)
        {
            if (round == null) return ObjectiveStatus.Pending;

            var target = ResolveTarget(round);
            if (round.CollectedValue.Value >= target)
            {
                if (!requireBoarding)
                    return ObjectiveStatus.Success;

                var connected = round.NetworkManager != null ? round.NetworkManager.ConnectedClientsIds.Count : 0;
                if (connected > 0 && round.PlayersBoarded.Value >= connected)
                    return ObjectiveStatus.Success;
            }

            if (failOnTimerExpired && round.HasActiveTimer && round.TimeRemaining.Value <= 0f
                && round.CollectedValue.Value < target)
            {
                return ObjectiveStatus.Failed;
            }

            return ObjectiveStatus.Pending;
        }

        public override string BuildHudStatus(RoundManager round)
        {
            if (round == null) return string.Empty;
            var target = ResolveTarget(round);
            var prefix = $"Collect: ${round.CollectedValue.Value} / ${target}";
            if (!requireBoarding) return prefix;

            var connected = round.NetworkManager != null ? round.NetworkManager.ConnectedClientsIds.Count : 0;
            var ready = connected > 0 && round.PlayersBoarded.Value >= connected;
            var boarding = ready ? "all aboard" : $"on launchpad {round.PlayersBoarded.Value}/{Mathf.Max(connected, 1)}";
            return $"{prefix}  |  {boarding}";
        }

        public override string BuildSuccessText(RoundManager round)
        {
            if (round == null) return "QUOTA MET.";
            var target = ResolveTarget(round);
            return $"QUOTA MET on {FormatPlanetLabel(round)}.\nCollected ${round.CollectedValue.Value} / ${target}.";
        }

        public override string BuildFailureText(RoundManager round)
        {
            if (round == null) return "QUOTA FAILED.";
            var target = ResolveTarget(round);
            return $"QUOTA FAILED on {FormatPlanetLabel(round)}.\nCollected ${round.CollectedValue.Value} / ${target} before time ran out.";
        }

        private int ResolveTarget(RoundManager round)
        {
            return quotaOverride > 0 ? quotaOverride : Mathf.Max(1, round.Quota.Value);
        }
    }
}
