using System;
using UnityEngine;

namespace Microdetail
{
    [Serializable]
    public struct LayerReference : IEquatable<LayerReference>
    {
        [SerializeField] private int layer;

        public int Index
        {
            get => layer;
            set => layer = Mathf.Clamp(value, 0, 31);
        }

        public string Name => (layer >= 0 && layer < 32) ? LayerMask.LayerToName(layer) : string.Empty;
        public bool IsValid => layer >= 0 && layer < 32;
        public override string ToString() => string.IsNullOrEmpty(Name) ? $"Layer {layer}" : Name;

        public static LayerReference FromName(string name)
        {
            var idx = LayerMask.NameToLayer(name);
            if (idx < 0) 
                idx = 0;
            
            return new LayerReference { layer = idx };
        }

        public static implicit operator int(LayerReference l) => l.layer;
        public static implicit operator LayerMask(LayerReference l) => 1 << l.layer;

        public bool Equals(LayerReference other) => layer == other.layer;
        public override bool Equals(object obj) => obj is LayerReference other && Equals(other);
        public override int GetHashCode() => layer;
    }
}