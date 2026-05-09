using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.TerrainTools;

namespace Microdetail
{
    public class BrushUIGroup : BaseBrushUIGroup
    {
        private static void OverrideDefaults(string name, float value)
        {
            var key = $"Microdetail.{name}";
            EditorPrefs.SetFloat(key, EditorPrefs.GetFloat(key, value));
        }

        private object CreateJitterHandler(float min, float max)
        {
            var jitterType = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                from type in assembly.GetTypes() 
                where type.Name == "BrushJitterHandler"
                select type).FirstOrDefault();
            
            return Activator.CreateInstance(jitterType, new object[] { 0.0f, min, max, false });
        }
        
        public BrushUIGroup(string name, Func<TerrainToolsAnalytics.IBrushParameter[]> analyticsCall = null) : base(name, analyticsCall)
        {
            OverrideDefaults("TerrainBrushSizeMin", 0.1f);
            OverrideDefaults("TerrainBrushSizeMax", 5.0f);
            
            AddScatterController(CreateValidator("BrushScatterVariator", "Microdetail", this, this, 0.0f) as IBrushScatterController);
            SetField("m_HasBrushScatter");

            var sizeController = CreateValidator("BrushSizeVariator", "Microdetail", this, this, 1.0f) as IBrushSizeController;
            AddSizeController(sizeController);
            var jitterHandler = CreateJitterHandler(0.0f, 5.0f);
            sizeController.GetType().GetField("m_JitterHandler", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sizeController, jitterHandler);
            SetField("m_HasBrushSize");
            
            AddRotationController(CreateValidator("BrushRotationVariator", "Microdetail", this, this, false, 0.0f) as IBrushRotationController);
            SetField("m_HasBrushRotation");
            
            AddStrengthController(CreateValidator("BrushStrengthVariator", "Microdetail", this, this, 1.0f) as IBrushStrengthController);
            SetField("m_HasBrushStrength");
            
            AddSpacingController(CreateValidator("BrushSpacingVariator", "Microdetail", this, this, 0.0f) as IBrushSpacingController);
            SetField("m_HasBrushSpacing");
            
            AddSmoothingController(CreateValidator("DefaultBrushSmoother", "Microdetail") as IBrushSmoothController);
        }

        private void SetField(string fieldName)
        {
            var field = GetType().GetField(fieldName, BindingFlags.Default | BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(this, true);
        }

        private object CreateValidator(string validator, params object[] args)
        {
            var typeToCreate = (from assembly in AppDomain.CurrentDomain.GetAssemblies()
                from type in assembly.GetTypes()
                where type.Name == validator
                select type).First();

            return Activator.CreateInstance(typeToCreate, args: args);
        }
    }
}