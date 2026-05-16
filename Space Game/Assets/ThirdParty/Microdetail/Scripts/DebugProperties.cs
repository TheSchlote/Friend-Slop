using System.Collections.Generic;

namespace Microdetail
{
    public struct DebugPropertyLayerDescriptor
    {
        public readonly Layer Layer;
        public readonly bool Temporary;

        public DebugPropertyLayerDescriptor(Layer layer, bool temporary)
        {
            Layer = layer;
            Temporary = temporary;
        }
    }
    
    public static class DebugProperties
    {
        public static bool UpdateBuffers { get; set; } = true;
        public static bool RenderFull { get; set; } = true;
        public static bool GetReadback { get; set; } = true;
        public static bool CollectData { get; set; } = false;
        
        public static readonly Dictionary<DebugPropertyLayerDescriptor, LayerStatisticData> LayersData = new Dictionary<DebugPropertyLayerDescriptor, LayerStatisticData>();

        public static void ClearData()
        {
            LayersData.Clear();
        }
    }
}