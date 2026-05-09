using Unity.Mathematics;
using UnityEngine;

namespace Microdetail
{
    [System.Serializable]
    public class LayerEntry
    {
        [SerializeField] private MicrodetailAsset asset;
        [SerializeField, HideInInspector] private float2 seed;
        [SerializeField] private float samplesPerUnitArea = 128;

        public float2 Seed
        {
            get => seed;
            set => seed = value;
        }

        public MicrodetailAsset Asset
        {
            get => asset;
            set => asset = value;
        }

        public float SamplesPerUnitArea
        {
            get => samplesPerUnitArea;
            set => samplesPerUnitArea = value;
        }
    }
}