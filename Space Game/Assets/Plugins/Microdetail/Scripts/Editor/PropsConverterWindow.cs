using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Microdetail
{
    public class PropsConverterWindow : EditorWindow
    {
        private enum MapType
        {
            Albedo,
            Metallic,
            AO,
            Smoothness
        }
        
        private enum Resolution
        {
            Resolution16 = 16,
            Resolution32 = 32,
            Resolution64 = 64,
            Resolution128 = 128,
            Resolution256 = 256,
            Resolution512 = 512
        }

        [SerializeField] private int padding = 2;
        [SerializeField] private float thickness = 0.1f;
        [SerializeField] private float size = 0.2f;
        [SerializeField] private GameObject objectToConvert;
        [SerializeField] private Resolution resolution = Resolution.Resolution64;
        [SerializeField] private string albedoSource = "_MainTex";
        
        [SerializeField] private string metallicSource = "_MaskMap";
        [SerializeField] private TextureChannelSource metallicTextureChannelSourceChannel = TextureChannelSource.R;
        [SerializeField] private Texture2D specificMetallicTexture;
        
        [SerializeField] private string aoSource = "_MaskMap";
        [SerializeField] private TextureChannelSource aoTextureChannelSourceChannel = TextureChannelSource.G;
        [SerializeField] private Texture2D specificAOTexture;
        
        [SerializeField] private string smoothnessSource = "_MaskMap";
        [SerializeField] private TextureChannelSource smoothnessTextureChannelSourceChannel = TextureChannelSource.A;
        [SerializeField] private Texture2D specificSmoothnessTexture;

        private string savePath;
        
        private Awaitable<ConversionResult> conversionTask;

        private SerializedObject serializedObject;
        
        [MenuItem("Tools/Microdetail/Converter")]
        public static void Open()
        {
            GetWindow<PropsConverterWindow>("Props Converter");            
        }
        
        public static Mesh MakeReadableMeshCopy(Mesh nonReadableMesh)
        {
            var meshCopy = new Mesh();
            meshCopy.indexFormat = nonReadableMesh.indexFormat;

            var verticesBuffer = nonReadableMesh.GetVertexBuffer(0);
            var totalSize = verticesBuffer.stride * verticesBuffer.count;
            var data = new byte[totalSize];
            verticesBuffer.GetData(data);
            meshCopy.SetVertexBufferParams(nonReadableMesh.vertexCount, nonReadableMesh.GetVertexAttributes());
            meshCopy.SetVertexBufferData(data, 0, 0, totalSize);
            verticesBuffer.Release();

            meshCopy.subMeshCount = nonReadableMesh.subMeshCount;
            var indexesBuffer = nonReadableMesh.GetIndexBuffer();
            var tot = indexesBuffer.stride * indexesBuffer.count;
            var indexesData = new byte[tot];
            indexesBuffer.GetData(indexesData);
            meshCopy.SetIndexBufferParams(indexesBuffer.count, nonReadableMesh.indexFormat);
            meshCopy.SetIndexBufferData(indexesData, 0, 0, tot);
            indexesBuffer.Release();

            var currentIndexOffset = 0u;
            for (var i = 0; i < meshCopy.subMeshCount; i++)
            {
                var subMeshIndexCount = nonReadableMesh.GetIndexCount(i);
                meshCopy.SetSubMesh(i, new SubMeshDescriptor((int)currentIndexOffset, (int)subMeshIndexCount));
                currentIndexOffset += subMeshIndexCount;
            }

            meshCopy.RecalculateNormals();
            meshCopy.RecalculateBounds();

            return meshCopy;
        }

        private MicrodetailAsset SaveAssetOrGetCurrent(SDFAsset asset, string path)
        {
            var current = AssetDatabase.LoadAssetAtPath<SDFAsset>(path);
            if (current == null)
            {
                asset.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(asset, path);
                current = asset;
            }
            
            return current == null ? asset : current;
        }

        private (Mesh Mesh, Dictionary<MapType, Texture> Textures) DrawMeshSelection()
        {
            var objectField = serializedObject.FindProperty(nameof(objectToConvert));
            EditorGUILayout.PropertyField(objectField);
            
            var gameObject = objectField.objectReferenceValue as GameObject;
            if (gameObject == null)
            {
                EditorGUILayout.HelpBox("Assign object to convert", MessageType.Error);
                return (null, null);
            }
            
            var lodGroups = gameObject.GetComponentsInChildren<LODGroup>();
            if (lodGroups.Length > 1)
            {
                EditorGUILayout.HelpBox("Object has more than one LOD group which is not supported.", MessageType.Error);
                return (null, null);
            }

            var renderers = new List<MeshRenderer>();
            if (lodGroups.Length == 0)
                gameObject.GetComponentsInChildren(renderers);
            else
            {
                if (lodGroups[0].lodCount == 0)
                {
                    EditorGUILayout.HelpBox("Object has an LOD group with zero lod count which is not supported.", MessageType.Error);
                    return (null, null);
                }

                var lods = lodGroups[0].GetLODs();
                var highestLOD = lods[0];
                var lodRenderers = new List<UnityEngine.Renderer>(highestLOD.renderers);
                if (lodRenderers.RemoveAll(x => !(x is MeshRenderer)) != 0)
                    EditorGUILayout.HelpBox("Object has some renderers besides MeshRenderers which are not supported. They will be skipped.", MessageType.Warning);

                renderers.AddRange(lodRenderers.ConvertAll(x => (MeshRenderer)x));
            }

            if (renderers.Count > 1)
            {
                EditorGUILayout.HelpBox("Object has more than one MeshRenderer which is not supported.", MessageType.Error);
                return (null, null);
            }

            var filter = renderers[0].GetComponent<MeshFilter>();
            if (filter == null)
            {
                EditorGUILayout.HelpBox("Object doesn't have a MeshFilter.", MessageType.Error);
                return (null, null);
            }
            
            var mesh = filter.sharedMesh;
            if (mesh == null)
            {
                EditorGUILayout.HelpBox("Object doesn't have a Mesh.", MessageType.Error);
                return (null, null);
            }

            var materials = renderers[0].sharedMaterials;
            if (materials.Length == 0)
            {
                EditorGUILayout.HelpBox("Object's MeshRenderer doesn't have any Materials.", MessageType.Error);
                return (null, null);
            }
            
            if (materials.Length > 1)
            {
                EditorGUILayout.HelpBox("Object's MeshRenderer has more than one Material/Sub mesh which is not suported.", MessageType.Error);
                return (null, null);
            }

            if (materials[0] == null)
            {
                EditorGUILayout.HelpBox("Object's material is null.", MessageType.Error);
                return (null, null);
            }

            var shader = materials[0].shader;
            if (shader == null)
            {
                EditorGUILayout.HelpBox("Material has no shader.", MessageType.Error);
                return (null, null);
            }
            
            DrawTextureSelectionDropdown("Albedo source", shader, albedoSource, false, x => albedoSource = x);

            EditorGUILayout.BeginHorizontal();
            DrawTextureSelectionDropdown("Metallic source", shader, metallicSource, true, x => metallicSource = x);
            metallicTextureChannelSourceChannel = (TextureChannelSource)EditorGUILayout.EnumPopup(string.Empty, metallicTextureChannelSourceChannel);
            specificMetallicTexture = (Texture2D)EditorGUILayout.ObjectField(string.Empty, specificMetallicTexture, typeof(Texture2D), true);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            DrawTextureSelectionDropdown("AO source", shader, aoSource, true, x => aoSource = x);
            aoTextureChannelSourceChannel = (TextureChannelSource)EditorGUILayout.EnumPopup(string.Empty, aoTextureChannelSourceChannel);
            specificAOTexture = (Texture2D)EditorGUILayout.ObjectField(string.Empty, specificAOTexture, typeof(Texture2D), true);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            DrawTextureSelectionDropdown("Smoothness source", shader, smoothnessSource, true, x => smoothnessSource = x);
            smoothnessTextureChannelSourceChannel = (TextureChannelSource)EditorGUILayout.EnumPopup(string.Empty, smoothnessTextureChannelSourceChannel);
            specificSmoothnessTexture = (Texture2D)EditorGUILayout.ObjectField(string.Empty, specificSmoothnessTexture, typeof(Texture2D), true);
            EditorGUILayout.EndHorizontal();
            
            if (!materials[0].HasProperty(albedoSource))
            {
                EditorGUILayout.HelpBox($"Object has no {albedoSource} property in it's material.", MessageType.Error);
                return (null, null);
            }

            var texture = materials[0].GetTexture(albedoSource);
            var textures = new Dictionary<MapType, Texture>();
            
            textures.Add(MapType.Albedo, texture);
            textures.Add(MapType.Metallic, GetTextureOrDefault(materials[0], metallicSource, Texture2D.blackTexture, specificMetallicTexture));
            textures.Add(MapType.Smoothness, GetTextureOrDefault(materials[0], smoothnessSource, Texture2D.blackTexture, specificSmoothnessTexture));
            textures.Add(MapType.AO, GetTextureOrDefault(materials[0], aoSource, Texture2D.whiteTexture, specificAOTexture));

            var meshResult = MakeReadableMeshCopy(mesh);
            if (texture != null) 
                return (meshResult, textures);
            
            EditorGUILayout.HelpBox($"Object has no texture assigned to {albedoSource} property of material.", MessageType.Error);
            
            return (null, null);
        }

        private Texture GetTextureOrDefault(Material material, string propertyName, Texture defaultValue, Texture specificTexture)
        {
            if (specificTexture != null)
                return specificTexture;
            
            var result = !material.HasProperty(propertyName) ? null : material.GetTexture(propertyName);
            return result == null ? defaultValue : result;
        }

        private void DrawTextureSelectionDropdown(string parameterName, Shader shader, string currentValue, bool allowNone, Action<string> setValueCallback)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField(new GUIContent(parameterName));
            
            var properties = new List<string>();
            var propertyNames = new List<string>();

            var thisName = string.Empty;
            for (var index = 0; index < shader.GetPropertyCount(); index++)
            {
                var propertyType = shader.GetPropertyType(index);
                if (propertyType != ShaderPropertyType.Texture)
                    continue;

                var property = shader.GetPropertyName(index);
                properties.Add(property);
                var propertyName = shader.GetPropertyDescription(index);
                propertyNames.Add(propertyName);
                if (currentValue == property)
                    thisName = propertyName;
            }

            if (thisName == string.Empty)
                thisName = $"(Missing) {currentValue}";

            if (!EditorGUILayout.DropdownButton(new GUIContent(thisName), FocusType.Keyboard))
            {
                EditorGUILayout.EndHorizontal();
                return;
            }

            var menu = new GenericMenu();

            if (allowNone)
                menu.AddItem(new GUIContent("none"), string.IsNullOrEmpty(currentValue), () => setValueCallback(null));
            
            for (var index = 0; index < properties.Count; index++)
            {
                var propertyName = propertyNames[index];
                var property = properties[index];
                var cachedProperty = property;
                menu.AddItem(new GUIContent(propertyName), cachedProperty == currentValue,
                    () => setValueCallback(cachedProperty));
                menu.ShowAsContext();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private string GetConversionStageName(ConversionStage stage)
        {
            switch (stage)
            {
                case ConversionStage.PreparingForConversion:
                    return "Preparing for conversion";
                case ConversionStage.GeneratingTextureSDF:
                    return "Generating Texture SDF";
                case ConversionStage.ReadingMesh:
                    return "Reading Mesh";
                case ConversionStage.GeneratingField:
                    return "Generating Field";
                case ConversionStage.FillingVoids:
                    return "Filling Voids";
                case ConversionStage.ReadingFieldTexture:
                    return "Reading Field Texture";
                case ConversionStage.ReadingAlbedoTexture:
                    return "Reading Albedo Texture";
                case ConversionStage.ReadingMaskTexture:
                    return "Reading Mask Texture";
                case ConversionStage.CleanUp:
                    return "Cleaning Up";
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
            }
        }

        public void OnGUI()
        {
            if (conversionTask == null)
            {
                if (GUILayout.Button("Clear references"))
                {
                    objectToConvert = null;
                    specificMetallicTexture = null;
                    specificSmoothnessTexture = null;
                    specificAOTexture = null;
                }
                
                serializedObject ??= new SerializedObject(this);
                serializedObject.Update();

                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(padding)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(thickness)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(resolution)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(size)));

                var objectConversionParameters = DrawMeshSelection();
                serializedObject.ApplyModifiedProperties();
                if (objectConversionParameters == (null, null))
                    return;

                if (!GUILayout.Button("Convert"))
                    return;

                var parameters = new ConversionData(objectConversionParameters.Mesh);
                parameters.Thickness = thickness;
                parameters.Padding = padding;
                parameters.VoxelSize = (int)resolution;
                parameters.Textures.Add(DefaultTextureIds.Albedo,
                    new TextureInfo(objectConversionParameters.Textures[MapType.Albedo] as Texture2D,
                        TextureChannelSource.None));
                parameters.Textures.Add(DefaultTextureIds.Metallic,
                    new TextureInfo(objectConversionParameters.Textures[MapType.Metallic] as Texture2D,
                        metallicTextureChannelSourceChannel));
                parameters.Textures.Add(DefaultTextureIds.Smoothness,
                    new TextureInfo(objectConversionParameters.Textures[MapType.Smoothness] as Texture2D,
                        smoothnessTextureChannelSourceChannel));
                parameters.Textures.Add(DefaultTextureIds.AO,
                    new TextureInfo(objectConversionParameters.Textures[MapType.AO] as Texture2D,
                        aoTextureChannelSourceChannel));

                var key = "MicrodetailAssetsSavePath";
                var path = EditorPrefs.GetString(key, Application.dataPath);
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                    path = "Assets";
            
                savePath = EditorUtility.SaveFilePanel("Item location", path, string.Empty, "asset");
                if (string.IsNullOrEmpty(savePath))
                    return;

                EditorPrefs.SetString(key, savePath);
                var converter = new PropsConverter();
                conversionTask = converter.Convert(parameters,
                    (stage, iteration, total) => EditorUtility.DisplayProgressBar("Conversion",
                        $"{GetConversionStageName(stage)} {iteration}/{total}", iteration / (float)total));
            }

            if (conversionTask != null && !conversionTask.GetAwaiter().IsCompleted)
                return;
            
            EditorUtility.ClearProgressBar();
            
            var projectPath = savePath.Substring(Application.dataPath.Length - "Assets".Length);

            var asset = CreateInstance<SDFAsset>();
            asset.UniformSize = new Curve(size);

            var children = AssetDatabase.LoadAllAssetsAtPath(projectPath);
            foreach (var child in children)
            {
                if (child is Module)
                    continue;

                if (!AssetDatabase.IsMainAsset(child))
                    Object.DestroyImmediate(child, true);
            }

            asset = SaveAssetOrGetCurrent(asset, projectPath) as SDFAsset;

            var result = conversionTask.GetAwaiter().GetResult();
            conversionTask = null;
            
            asset.SDF = result.Results[DefaultTextureIds.SDF].Texture;
            
            AssetDatabase.AddObjectToAsset(asset.SDF, asset);
            
            asset.Albedo = result.Results[DefaultTextureIds.Albedo].Texture;
            AssetDatabase.AddObjectToAsset(asset.Albedo, asset);
            
            asset.Mask = result.Results[DefaultTextureIds.Mask].Texture;
            AssetDatabase.AddObjectToAsset(asset.Mask, asset);

            AssetDatabase.SaveAssets();
        }

        private void Update()
        {
            Repaint();
        }
    }
}