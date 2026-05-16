using UnityEngine;

namespace Microdetail
{
    public class RangeSliderAttribute : PropertyAttribute
    {
        public float MinLimit;
        public float MaxLimit;

        public RangeSliderAttribute(float minLimit, float maxLimit)
        {
            MinLimit = minLimit;
            MaxLimit = maxLimit;
        }
    }
}