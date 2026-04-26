using UnityEngine;

namespace FriendSlop.Round
{
    [CreateAssetMenu(menuName = "Friend Slop/Objectives/Survival", fileName = "SurvivalObjective")]
    public class SurvivalObjective : RoundObjective
    {
        [SerializeField] private float survivalSeconds = 120f;
        [SerializeField] private bool requireBoardingOnSurvive = false;

        public override void ServerInitialize(RoundManager round)
        {
            if (round == null) return;
            round.ServerSetTimer(Mathf.Max(1f, survivalSeconds));
        }

        public override ObjectiveStatus Evaluate(RoundManager round)
        {
            if (round == null) return ObjectiveStatus.Pending;
            if (round.TimeRemaining.Value > 0f) return ObjectiveStatus.Pending;

            if (!requireBoardingOnSurvive) return ObjectiveStatus.Success;

            var connected = round.NetworkManager != null ? round.NetworkManager.ConnectedClientsIds.Count : 0;
            return connected > 0 && round.PlayersBoarded.Value >= connected
                ? ObjectiveStatus.Success
                : ObjectiveStatus.Failed;
        }

        public override string BuildHudStatus(RoundManager round)
        {
            if (round == null) return string.Empty;
            return $"Survive: {RoundManager.FormatTime(round.TimeRemaining.Value)}";
        }
    }
}
