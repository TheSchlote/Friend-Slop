using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace Microdetail
{
    [ScriptedImporter(1, "psdf", 10000)]
    public class ProceduralMaterialImporter : ScriptedImporter
    {
        [SerializeField] private Shader microdetailShader;
        [SerializeField] private TextAsset shaderSource;
        [SerializeField] private TextAsset structuresSource;
        [SerializeField] private TextAsset terrainSourceInclude;
        [SerializeField] private TextAsset commonSourceInclude;
        [SerializeField] private TextAsset sdfSourceInclude;
        [SerializeField] private TextAsset noiseSourceInclude;
        [SerializeField] private Material referenceMaterial;
        
        private string GetSource<T>(Object target) where T : Object
        {
            var mainSourcePath = AssetDatabase.GetAssetPath(target);
            var fullPath = Path.Combine(Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length), mainSourcePath);
            return File.ReadAllText(fullPath);
        }
        
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var sampleSource = File.ReadAllText(ctx.assetPath);

            ctx.DependsOnSourceAsset(AssetDatabase.GetAssetPath(microdetailShader));
            var templateSource = GetSource<Shader>(microdetailShader);
            var functionsSource = GetSource<TextAsset>(shaderSource);
            var structuresSourceCode = GetSource<TextAsset>(structuresSource);
            var terrainSource = GetSource<TextAsset>(terrainSourceInclude);
            var commonSource = GetSource<TextAsset>(commonSourceInclude);
            var sdfSource = GetSource<TextAsset>(sdfSourceInclude);
            var noiseSource = GetSource<TextAsset>(noiseSourceInclude);

            var newSource = templateSource
                .Replace("#include \"Shader.cginc\"", functionsSource)
                .Replace("#include \"Sample.cginc\"", sampleSource)
                .Replace("#include \"Structures.cginc\"", structuresSourceCode)
                .Replace("#include \"Terrain.cginc\"", terrainSource)
                .Replace("Shader \"Microdetail sdf\"", $"Shader \"{Path.GetFileNameWithoutExtension(ctx.assetPath)}\"")
                .Replace("#include \"Common.cginc\"", commonSource)
                .Replace("#include \"SDF.cginc\"", sdfSource)
                .Replace("#include \"Noise.cginc\"", noiseSource);
            
            var shaderAsset = ShaderUtil.CreateShaderAsset(newSource, true);
            shaderAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " shader";
            var material = Instantiate(referenceMaterial);
            material.shader = shaderAsset;
            material.name = Path.GetFileNameWithoutExtension(ctx.assetPath) + " material";
            ctx.AddObjectToAsset("Material", material);
            ctx.SetMainObject(material);

            var transformedShaderSource = new TextAsset(newSource);
            ctx.AddObjectToAsset("TransformedSource.shader", transformedShaderSource);

            ctx.AddObjectToAsset("Shader", shaderAsset);
        }
    }
}