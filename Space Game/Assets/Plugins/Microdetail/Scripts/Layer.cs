using System.Collections.Generic;
using UnityEngine;

namespace Microdetail
{
    public class Layer : ScriptableObject
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private bool visible = true;
        [SerializeField] private List<LayerEntry> entries = new List<LayerEntry>();

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public bool Visible
        {
            get => visible;
            set => visible = value;
        }
        
        public List<LayerEntry> Entries => entries;
    }
}