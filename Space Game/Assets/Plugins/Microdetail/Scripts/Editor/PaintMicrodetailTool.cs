using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEditorInternal;
using UnityEngine;

namespace Microdetail
{
    public class PaintMicrodetailTool : MicrodetailTool<PaintMicrodetailTool>
    {
        private static Texture2D icon;
        
        private Material material;

        public override string OnIcon => PluginPath + "Editor/Resources/Microdetail/Icons/Paint.png";
        public override string OffIcon => OnIcon;
        
        protected override Material Material
        {
            get
            {
                if (material == null)
                    material = new Material(Resources.Load<Shader>("Microdetail/Shaders/PaintMicrodetail"));
                
                return material;
            }
        }

        protected override string ClearName => "Clear details";

        protected override MapSet GetMapSet(LayerRenderer renderer)
        {
            return renderer.DensityMapSet;
        }

        protected override void Clear()
        {
            var selected = Palette.SelectedLayer;
            if (selected == null)
                return;

            ClearUtility.Clear(selected, x => x.DensityMapSet, Texture2D.blackTexture, StringConstants.ClearMicrodetail + " Density");
        }

        public override string GetName()
        {
            return "Paint Microdetail";
        }

        public override string GetDescription()
        {
            return "Provides an ability to draw microdetails on the terrain.";
        }
    }
}