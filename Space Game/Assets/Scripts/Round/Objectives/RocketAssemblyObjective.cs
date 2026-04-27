using UnityEngine;

namespace FriendSlop.Round
{
    [CreateAssetMenu(menuName = "Friend Slop/Objectives/Rocket Assembly", fileName = "RocketAssemblyObjective")]
    public class RocketAssemblyObjective : RoundObjective
    {
        [SerializeField] private bool failOnTimerExpired = true;
        [SerializeField] private int minQuotaToAvoidFail = 0;

        public override ObjectiveStatus Evaluate(RoundManager round)
        {
            if (round == null) return ObjectiveStatus.Pending;

            var connected = round.NetworkManager != null ? round.NetworkManager.ConnectedClientsIds.Count : 0;
            if (RoundStateUtility.IsLaunchReady(round.RocketAssembled.Value, round.PlayersBoarded.Value, connected))
                return ObjectiveStatus.Success;

            if (failOnTimerExpired && round.HasActiveTimer && round.TimeRemaining.Value <= 0f
                && round.CollectedValue.Value < Mathf.Max(round.Quota.Value, minQuotaToAvoidFail))
            {
                return ObjectiveStatus.Failed;
            }

            return ObjectiveStatus.Pending;
        }

        public override string BuildHudStatus(RoundManager round)
        {
            if (round == null) return string.Empty;
            return $"Parts: {Format(round.HasCockpit.Value, "Cockpit")} | {Format(round.HasWings.Value, "Wings")} | {Format(round.HasEngine.Value, "Engine")}";
        }

        private static string Format(bool installed, string label)
        {
            return installed ? $"{label} OK" : $"{label} missing";
        }
    }
}
