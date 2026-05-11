using UnityEngine;

namespace FriendSlop.Interiors
{
    public partial class InteriorSceneBootstrapper
    {
        private void DestroyInterior()
        {
            if (_interiorRoot != null)
            {
                Destroy(_interiorRoot);
                _interiorRoot = null;
            }
            if (_minimap != null)
            {
                Destroy(_minimap.gameObject);
                _minimap = null;
            }
        }
    }
}
