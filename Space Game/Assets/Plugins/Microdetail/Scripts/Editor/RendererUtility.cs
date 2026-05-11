using UnityEngine;

namespace Microdetail
{
    public static class RendererUtility
    {
        public static MicrodetailRenderer GetMicrodetailRenderer(Terrain terrain)
        {
            var microdetailBuilder = terrain.GetComponentInChildren<MicrodetailRenderer>();
            if (microdetailBuilder != null)
            {
                microdetailBuilder.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;
                return microdetailBuilder;
            }

            var microdetailGameObject = new GameObject("Microdetail");
            microdetailGameObject.transform.SetParent(terrain.transform);
            
            microdetailBuilder = microdetailGameObject.AddComponent<MicrodetailRenderer>();
            microdetailBuilder.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;

            return microdetailBuilder;
        }
    }
}