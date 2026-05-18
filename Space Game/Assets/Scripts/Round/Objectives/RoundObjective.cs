using UnityEngine;

namespace FriendSlop.Round
{
    public abstract class RoundObjective : ScriptableObject
    {
        [SerializeField] private string title = "Objective";
        [TextArea] [SerializeField] private string description;

        public string Title => string.IsNullOrWhiteSpace(title) ? name : title;
        public string Description => description;

        // Called once on the server when ServerStartRound prepares a new round.
        // Use this to seed timer/quota or any custom state on the RoundManager.
        public virtual void ServerInitialize(RoundManager round) { }

        // Called every server tick while RoundPhase is Active. Returning Success
        // ends the round in RoundPhase.Success; Failed ends in RoundPhase.Failed.
        public abstract ObjectiveStatus Evaluate(RoundManager round);

        // HUD progress string shown while the round is active. Returning empty
        // hides the line.
        public virtual string BuildHudStatus(RoundManager round) => string.Empty;

        // True while the primary win condition is satisfied but the crew still
        // has to board/extract. Drives the on-screen extraction banner. Read-only
        // over server-replicated state; never mutates round authority.
        public virtual bool IsExtractionReady(RoundManager round) => false;

        // Loud call-to-action shown the moment IsExtractionReady flips true.
        public virtual string BuildExtractionBanner(RoundManager round) => "LAUNCHPAD ACTIVE - BOARD TO EXTRACT";

        public abstract string BuildSuccessText(RoundManager round);
        public abstract string BuildFailureText(RoundManager round);

        protected static string FormatPlanetLabel(RoundManager round)
        {
            var planet = round != null ? round.CurrentPlanet : null;
            return planet != null ? $"{planet.DisplayName} (Tier {planet.Tier})" : "Unknown planet";
        }

        protected static bool AllConnectedBoarded(RoundManager round)
        {
            if (round == null) return false;
            var connected = round.NetworkManager != null ? round.NetworkManager.ConnectedClientsIds.Count : 0;
            return connected > 0 && round.PlayersBoarded.Value >= connected;
        }
    }
}
