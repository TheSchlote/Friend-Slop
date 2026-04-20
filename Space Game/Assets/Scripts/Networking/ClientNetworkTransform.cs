using Unity.Netcode.Components;

namespace FriendSlop.Networking
{
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}
