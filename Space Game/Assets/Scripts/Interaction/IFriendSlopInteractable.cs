using FriendSlop.Player;

namespace FriendSlop.Interaction
{
    public interface IFriendSlopInteractable
    {
        bool CanInteract(NetworkFirstPersonController player);
        string GetPrompt(NetworkFirstPersonController player);
        void Interact(NetworkFirstPersonController player);
    }
}
