using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Microdetail
{
    public class TintMicrodetailTool : MicrodetailTool<TintMicrodetailTool>
    {
        private static Texture2D icon;
        private static readonly int Color = Shader.PropertyToID("_Color");
        [SerializeField] private Color color = new Color(0.56f, 0.56f, 0.56f);

        public override int IconIndex => 1;

        public override string OnIcon => PluginPath + "Editor/Resources/Microdetail/Icons/Tint.png";
        public override string OffIcon => OnIcon;

        protected override string ClearName => "Clear tint";

        public override string GetName()
        {
            return "Tint Microdetail";
        }

        public override string GetDescription()
        {
            return "Provides an ability to tint microdetails on the terrain.";
        }

        private Material material;

        protected override Material Material
        {
            get
            {
                if (material == null)
                    material = new Material(Resources.Load<Shader>("Microdetail/Shaders/ColorMicrodetail"));
                
                material.SetColor(Color, color);
                return material;
            }
        }

        protected override MapSet GetMapSet(LayerRenderer renderer)
        {
            return renderer.TintMapSet;
        }

        protected override void DrawSettings(Terrain terrain)
        {
            color = EditorGUILayout.ColorField(new GUIContent("Tint color"), color, true, false, false);
            base.DrawSettings(terrain);
        }

        protected override void Clear()
        {
            var selected = Palette.SelectedLayer;
            if (selected == null)
                return;

            ClearUtility.Clear(selected, x => x.TintMapSet, Texture2D.whiteTexture, StringConstants.ClearMicrodetail + " Tint");
        }
    }
}