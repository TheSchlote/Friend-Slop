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
    }
}
