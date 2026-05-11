using System;
using System.Collections.Generic;
using UnityEngine;

namespace Microdetail
{
    public class ResourcesAllocator : IDisposable
    {
        private Dictionary<LayerEntry, DrawingResources> drawingResources = new Dictionary<LayerEntry, DrawingResources>();

        public DrawingResources GetResources(LayerEntry layerEntry, uint count)
        {
            drawingResources ??= new Dictionary<LayerEntry, DrawingResources>();
            if (!drawingResources.TryGetValue(layerEntry, out var resources))
            {
                resources = new DrawingResources(count * 2);
                drawingResources.Add(layerEntry, resources);
            }

            if (resources.PropsCount < count)
                resources.Resize(count);

            return resources;
        }

        public void ReloadPlacementComputeShader()
        {
            foreach (var drawingResource in drawingResources)
                drawingResource.Value.ReloadPlacementComputeShader();
        }

        public void Dispose()
        {
            foreach (var resource in drawingResources)
                resource.Value.Dispose();
        }
    }
}