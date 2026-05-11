using System.Collections.Generic;

namespace Microdetail
{
   public static class Settings
   {
      private static Dictionary<Layer, float> entriesPerUnitAreaScaler = new Dictionary<Layer, float>();

      private static bool enabled = true;

      public static float GetLayerEntriesPerUnitAreaScaler(Layer layer)
      {
         return entriesPerUnitAreaScaler.GetValueOrDefault(layer, 1.0f);
      }

      public static void SetLayerEntriesPerUnitAreaScaler(Layer layer, float value)
      {
         entriesPerUnitAreaScaler[layer] = value;
      }

      public static bool IsEnabled() => enabled;
      public static void SetEnabled(bool newValue) => enabled = newValue;
   }
}