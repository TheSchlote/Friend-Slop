using UnityEngine;

namespace FriendSlop.Round
{
    [CreateAssetMenu(menuName = "Friend Slop/Objectives/Survival", fileName = "SurvivalObjective")]
    public class SurvivalObjective : RoundObjective
    {
        [SerializeField] private float survivalSeconds = 120f;
        [SerializeField] private bool requireBoardingOnSurvive = false;
        // Grace window after survivalSeconds expires when requireBoardingOnSurvive is true:
        // the round stays Active so players can sprint to the launchpad. Failure is only
        // declared if not everyone is boarded once this window also expires.
        [SerializeField, Min(0f)] private float extractionGraceSeconds = 30f;

        public override void ServerInitialize(RoundManager round)
        {
            if (round == null) return;
            round.ServerSetTimer(Mathf.Max(1f, survivalSeconds));
        }

        public override ObjectiveStatus Evaluate(RoundManager round)
        {
            if (round == null) return ObjectiveStatus.Pending;

            if (!round.IsExtractionWindow.Value)
            {
                if (round.TimeRemaining.Value > 0f) return ObjectiveStatus.Pending;
                if (!requireBoardingOnSurvive) return ObjectiveStatus.Success;

                // Survival timer ran out and boarding is required. Open an extraction window
                // so players have a chance to reach the pad before resolution.
                if (extractionGraceSeconds > 0f)
                {
                    round.ServerOpenExtractionGrace(extractionGraceSeconds);
                    return ObjectiveStatus.Pending;
                }

                return AllPlayersBoarded(round) ? ObjectiveStatus.Success : ObjectiveStatus.Failed;
            }

            // Extraction window: anyone who reaches the pad early closes the round; otherwise
            // failure is only declared when this second timer expires.
            if (AllPlayersBoarded(round)) return ObjectiveStatus.Success;
            if (round.TimeRemaining.Value > 0f) return ObjectiveStatus.Pending;
            return AllPlayersBoarded(round) ? ObjectiveStatus.Success : ObjectiveStatus.Failed;
        }

        public override string BuildHudStatus(RoundManager round)
        {
            if (round == null) return string.Empty;
            if (round.IsExtractionWindow.Value)
                return $"EXTRACT: {RoundManager.FormatTime(round.TimeRemaining.Value)} - reach the pad!";
            return $"Survive: {RoundManager.FormatTime(round.TimeRemaining.Value)}";
        }

        public override string BuildSuccessText(RoundManager round)
        {
            var suffix = requireBoardingOnSurvive ? "Crew survived and boarded." : "Crew survived the timer.";
            return round != null
                ? $"SURVIVED on {FormatPlanetLabel(round)}.\n{suffix}"
                : "SURVIVED.";
        }

        public override string BuildFailureText(RoundManager round)
        {
            return round != null
                ? $"SURVIVAL FAILED on {FormatPlanetLabel(round)}.\nTimer expired before everyone boarded."
                : "SURVIVAL FAILED.";
        }

        private static bool AllPlayersBoarded(RoundManager round)
        {
            var connected = round.NetworkManager != null ? round.NetworkManager.ConnectedClientsIds.Count : 0;
            return connected > 0 && round.PlayersBoarded.Value >= connected;
        }
    }
}
