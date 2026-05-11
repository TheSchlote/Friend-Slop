using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Microdetail
{
    public abstract class MicrodetailAsset : ScriptableObject
    {
        private static readonly int Rotation = Shader.PropertyToID("InitialRotation");

        [SerializeField] private bool enabled = true;
        
        [SerializeField] private List<Module> modules = new List<Module>();
        
        [FormerlySerializedAs("uniformUniformSize")] [SerializeField] private Curve uniformSize = new Curve(0.1f);
        [SerializeField] private Gradient tintOverSize = new Gradient();
        [SerializeField] private Gradient randomTint = new Gradient();
        
        [Header("Culling settings")]
        [SerializeField] private float drawingDistance = 50.0f;
        [SerializeField] private Curve fadeOverDistance = new Curve(1.0f, 0.0f);

        [Header("Positioning settings")] [SerializeField] private float3 initialRotation;
        [SerializeField, Range(0.0f, 1.0f)] private float terrainAlignFactor = 1.0f;

        [SerializeField] private float3 centerShift = 0.0f;

        [SerializeField, Range(0.0f, 1.0f)] private float flipChance = 0.0f;
        [SerializeField, RangeSlider(0.0f, 180.0f)] private Range planarRotationRange = new Range(0.0f, 30.0f);
        [SerializeField, RangeSlider(0.0f, 360.0f)] private Range normalRotationRange = new Range(0.0f, 360.0f);
        [SerializeField, RangeSlider(-1.0f, 1.0f)] private Range normalShift = new Range(-0.1f, 0.1f);

        [Header("Rendering settings")]
        [SerializeField] private int renderingQueue = 2500;
        [SerializeField] private bool castShadows = false;
        [SerializeField] private ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.Off;
        [SerializeField] private RenderingLayerMask renderingLayerMask = -1;
        [SerializeField] private LayerReference layer;
        
        public RenderingLayerMask RenderingLayerMask
        {
            get => renderingLayerMask;
            set => renderingLayerMask = value;
        }

        public bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }

        public int RenderQueue
        {
            get => renderingQueue;
            set
            {
                renderingQueue = value;
                if (Material != null)
                    Material.renderQueue = renderingQueue;
            }
        }

        public int Layer
        {
            get => layer.Index;
            set => layer.Index = value;
        }
        
        public Gradient TintOverSize => tintOverSize;
        public Gradient RandomTint => randomTint;

        public float3 InitialRotation
        {
            get => initialRotation;
            set => initialRotation = value;
        }
        
        public Curve UniformSize
        {
            get => uniformSize;
            set => uniformSize = value;
        }

        public ReflectionProbeUsage ReflectionProbeUsage
        {
            get => reflectionProbeUsage;
            set => reflectionProbeUsage = value;
        }
        
        public Curve FadeOverDistance => fadeOverDistance;

        public abstract Material Material { get; }
        
        public abstract float3 AspectRatio { get; }

        public float TerrainAlignFactor
        {
            get => terrainAlignFactor;
            set => terrainAlignFactor = value;
        }

        public float FlipChance
        {
            get => flipChance;
            set => flipChance = value;
        }

        public bool CastShadows
        {
            get => castShadows;
            set => castShadows = value;
        }

        public float DrawingDistance
        {
            get => drawingDistance;
            set => drawingDistance = Mathf.Max(value, 0.0f);
        }

        public float3 CenterShift
        {
            get => centerShift;
            set => centerShift = value;
        }

        public Range PlanarRotationRange
        {
            get => planarRotationRange;
            set => planarRotationRange = value;
        }

        public Range NormalRotationRange
        {
            get => normalRotationRange;
            set => normalRotationRange = value;
        }

        public Range NormalShift
        {
            get => normalShift;
            set => normalShift = value;
        }
        
        public int ModulesCount => modules.Count;

        public Module GetModule(int index) => modules[index];
        public void AddModule(Module module) => modules.Add(module);
        public void RemoveModule(Module module) => modules.Remove(module);

        public virtual void PrepareForRendering(Material material, MaterialPropertyBlock propertyBlock, TerrainGraphicalInfo graphicalInfo)
        {
            Material.renderQueue = RenderQueue;
            foreach (var module in modules)
                if (module is IRenderingPrepareCallbackHandler handler)
                    handler.PrepareForRendering(material, propertyBlock, graphicalInfo);
        }

        public void SetupDefaultPopulateShader(int kernelIndex, ComputeShader shader)
        {
            shader.SetVector(Rotation, (Vector3) math.radians(initialRotation));
            
            foreach (var module in modules)
                if (module is IDefaultPopulateShaderSetupper setupper)
                    setupper.SetupBuiltInPopulateShader(kernelIndex, shader);
        }
    }
}