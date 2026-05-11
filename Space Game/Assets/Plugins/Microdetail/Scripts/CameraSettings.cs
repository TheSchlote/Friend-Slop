using UnityEngine;

namespace Microdetail
{
    public class CameraSettings : MonoBehaviour
    {
        [Tooltip("Microdetails won't be rendered for this camera")] [SerializeField] private bool excludeFromRendering = false;

        public bool ExcludeFromRendering
        {   
            get => excludeFromRendering;
            set => excludeFromRendering = value;
        }
    }
}