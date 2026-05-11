#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#define SQUARE_ROOT_3 1.73205080757f
#define HALF_SQUARE_ROOT_3 (SQUARE_ROOT_3 / 2.0f)

#if SHADERPASS == SHADERPASS_FORWARD || SHADERPASS == SHADERPASS_DEPTHNORMALSONLY || SHADERPASS == SHADERPASS_GBUFFER
#define TANGENT_INPUT input.tangentOS
#else
#define TANGENT_INPUT fakeTangent
#endif

#include "UnityIndirect.cginc"
#include "Structures.cginc"
#include "Common.cginc"
#include "Noise.cginc"

sampler2D _HeightMap;
TEXTURE2D_ARRAY(_SplatMaps);
SAMPLER(sampler_Linear_Clamp_SplatMaps);
TEXTURE2D_ARRAY(_Textures);
SAMPLER(sampler_Linear_Repeat_Textures);
TEXTURE2D_ARRAY(_MaskMaps);
SAMPLER(sampler_Linear_Repeat_MaskMaps);
TEXTURE2D_ARRAY(_NormalMaps);
SAMPLER(sampler_Linear_Repeat_NormalMaps);
float3 _TerrainPosition;
float3 _TerrainSize;
float2 _HeightMapSize;
float4 _SplatmapSize;
int _SplatMapsCount;
int _LayersCount;
float4 _SplatMapPositioning[32];
float4 _MinRemap[32];
float4 _MaxRemap[32];
float4 _BlendDefaults[32];
float _TerrainBlendAmount = 0.02f;
float _TerrainBlendSmoothingLength = 0.02f;
float3 _NormalizedSize;
float _SlicesCount;

#if defined(MICRODETAIL_PREVIEW)
float4x4 _PreviewTransformation;
#endif

StructuredBuffer<Transformation> _MicrodetailTransformations;
StructuredBuffer<MicrodetailProperties> _MicrodetailProperties;

inline MicrodetailProperties ReadProperties(int instanceID)
{
#if defined(MICRODETAIL_PREVIEW)
    MicrodetailProperties properties;
    properties.Tint = 1.0f;
    properties.Lod = 0.0f;
#if defined(MICRODETAIL_TERRAIN_BLENDING)
    properties.Normal = PackFloat4ToUint(float4(-1.0f, 0.0f, -1.0f, 0.0f));
#endif

    return properties;
#else
    return _MicrodetailProperties[instanceID];
#endif
}

inline Transformation ReadTransformation(int instanceID)
{
#if defined(MICRODETAIL_PREVIEW)
    Transformation transform;
    transform.Transformation = RevertCameraTranslationFromMatrix(UNITY_MATRIX_M);

    return transform;
#else
    return _MicrodetailTransformations[instanceID];
#endif
}

float4x4 InvertNonUniformTransform(float4x4 transform)
{
    float3 scale = float3(
            length(transform[0].xyz),
            length(transform[1].xyz),
            length(transform[2].xyz) 
        );

    float3 invScale = 1.0 / scale;

    float3x3 rotation = float3x3(
            transform[0].xyz * invScale.x,
            transform[1].xyz * invScale.y,
            transform[2].xyz * invScale.z
        );

    float3x3 invRotation = transpose(rotation);

    float3 translation = transform[3].xyz;
    float3 invTranslation = -mul(invRotation, translation * invScale);

    float4x4 inverseTransform = float4x4(
            float4(invRotation[0] * invScale.x, 0),
            float4(invRotation[1] * invScale.y, 0),
            float4(invRotation[2] * invScale.z, 0),
            float4(invTranslation, 1)
        );

    return inverseTransform;
}

inline float4 SampleHeightTexture(float2 uv)
{
    float2 shift = 0.5f / _HeightMapSize;
    return UnpackHeightmap(tex2D(_HeightMap, (floor(uv * _HeightMapSize + 0.01f) / _HeightMapSize) + shift));
}

#include "Terrain.cginc"

#include "SDF.cginc"

inline Ray CreateRay(Transformation transform, float3 VertexPosition, float4 LocalPosition, float3 scale)
{
    float3 worldPosition = VertexPosition.xyz;
#if (SHADEROPTIONS_CAMERA_RELATIVE_RENDERING != 0)
    worldPosition -= _WorldSpaceCameraPos.xyz;
#endif

    float3 worldSpaceViewDir = GetWorldSpaceViewDir(worldPosition);
    Ray result;
    float3x3 inversedTransformation = (float3x3)InvertNonUniformTransform(transform.Transformation);
    result.direction = -normalize(normalize(mul(inversedTransformation, worldSpaceViewDir)) * scale);
    result.origin = (LocalPosition.xyz / LocalPosition.w) + 0.5f;

    return result;
}

inline uint ReadInstanceID(int instanceID)
{
#if defined(MICRODETAIL_PREVIEW)
    return 0;
#else
    InitIndirectDrawArgs(0);
    return GetIndirectInstanceID(instanceID);
#endif
}

struct TerrainSampleResult
{
    float4 Albedo;
    float3 Normal;
    float Smoothness;
    float Metallic;
    float AO;
};

inline void Vertex(inout int InstanceID, inout float3 VertexPosition, inout float3 Normal, inout float4 Tangent, out float4 LocalPosition, out float3 ColorShift, out float3 ViewDirection)
{
    ColorShift = 0.0f;
    ViewDirection = 0.0f;
#if !defined(MICRODETAIL_PREVIEW)
    uint instanceID = ReadInstanceID(InstanceID);
    Transformation transform = ReadTransformation(instanceID);
#endif

    LocalPosition = float4(sign(VertexPosition) * 0.5f, 1.0f);
    
#if !defined(MICRODETAIL_PREVIEW)
    VertexPosition = mul(transform.Transformation, float4(VertexPosition, 1.0f)).xyz;
    Normal = normalize(mul(transform.Transformation, float4(Normal, 0.0f)).xyz);
    float w = Tangent.w;
    Tangent.xyz = normalize(mul((float3x3)transform.Transformation, Tangent.xyz));
    Tangent.w = w;
#endif
}

inline TerrainSampleResult GetTerrainProperties(float2 uv)
{
    int mapIndex = 0;
    float2 transformedUV = float2(uv.x, 1.0f - uv.y);
    TerrainSampleResult resultData;
    resultData.Albedo = 0.0f;
    resultData.Normal = 0.0f;

    float4 maskOutput = 0.0f;

    float2 splatmapUV = (transformedUV * (_SplatmapSize.zw - 1.0f) + 0.5f) * _SplatmapSize.xy;
    for (int splatIndex = 0; splatIndex < _SplatMapsCount && mapIndex < _LayersCount; splatIndex++)
    {
        float4 splatValues = SAMPLE_TEXTURE2D_ARRAY(_SplatMaps, sampler_Linear_Clamp_SplatMaps, splatmapUV, splatIndex);

        UNITY_UNROLL
        for (int innerIndex = 0; innerIndex < 4 && mapIndex < _LayersCount; innerIndex++, mapIndex++)
        {
            float4 positioning = _SplatMapPositioning[mapIndex];
            float2 uvToSample = uv * positioning.xy + positioning.zw;
            float4 textureValue = SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Linear_Repeat_Textures, uvToSample, mapIndex);
            float4 normalValue  = SAMPLE_TEXTURE2D_ARRAY(_NormalMaps, sampler_Linear_Repeat_NormalMaps, uvToSample, mapIndex);
            float4 maskValue    = SAMPLE_TEXTURE2D_ARRAY(_MaskMaps, sampler_Linear_Repeat_MaskMaps, uvToSample, mapIndex);
            float scaler = splatValues[innerIndex];
            resultData.Albedo += textureValue * scaler;
            resultData.Normal += (UnpackNormal(normalValue) * scaler).xyz;
            maskOutput += (maskValue * _MaxRemap[mapIndex] + _MinRemap[mapIndex]) * scaler * _BlendDefaults[mapIndex];
        }
    }

    resultData.Normal.xy *= 2.0f;
    resultData.Normal = normalize(resultData.Normal);
    resultData.Metallic = maskOutput.x;
    resultData.AO = pow(abs(maskOutput.y), 1.0f / 2.2f);
    resultData.Smoothness = maskOutput.w;
    
    return resultData;
}

inline float3 RestoreNormal(half2 values)
{
    return half3(values.x, sqrt(1.0f - dot(values, values)), values.y);
}

inline void Fragment(float3x3 WorldToTangent, int InstanceID, float4 LocalPosition, float4 Position, out float3 Color, out float3 Normal, float NormalSpherization, out float Smoothness, out float Metallic, out float AO, out float DepthOffset)
{
    Normal = 1.0f;
    DepthOffset = 0.0f;

    Color = 1;
    AO = 1.0f;
    Smoothness = 0.0f;
    Metallic = 0.0f;

    uint instanceID = ReadInstanceID(InstanceID);
    Transformation transform = ReadTransformation(instanceID);
    MicrodetailProperties properties = ReadProperties(instanceID);

    float3 inversedSize = normalize(GetVolumeSize());
    inversedSize /= min(inversedSize.x, min(inversedSize.y, inversedSize.z));

    float3 worldPosition = Position.xyz / Position.w;

#if defined(MICRODETAIL_PREVIEW)
    float4 tint = 1.0f;
#else
    float4 tint = UnpackUintToFloat4(properties.Tint);
#endif

    if (hash13(worldPosition * float3(1.23f, 2.1123f, 43.12312)) > tint.a)
        discard;
    
    Ray ray = CreateRay(transform, worldPosition, LocalPosition, inversedSize);

    float lod = properties.Lod;
    IntersectionResult result = FindIntersection(ray, lod, pow(2.0f, lod), NormalSpherization);

    float3 albedo;
    Sample(float4(result.Position, lod), albedo, Metallic, Smoothness, AO);

    Color = albedo * tint.rgb;

    float4x4 transformationMatrix = transform.Transformation;
    
    float3 initialPosition = mul(transformationMatrix, float4((ray.origin - 0.5f) * _NormalizedSize, 1.0f)).xyz;
    float3 currentPosition = mul(transformationMatrix, float4((result.Position - 0.5f) * _NormalizedSize, 1.0f)).xyz;

    float depthDifference = distance(currentPosition, initialPosition);
    DepthOffset = depthDifference;
    
    float4 worldSpaceNormal = normalize(mul(transformationMatrix, float4(result.Normal, 0.0f)));
    
#if defined(MICRODETAIL_TERRAIN_BLENDING)
    float3 position = float3(transformationMatrix[0][3], transformationMatrix[1][3], transformationMatrix[2][3]);
    float3 terrainRelatedPosition = (mul(transformationMatrix, float4((result.Position - 0.5f) * _NormalizedSize, 0.0f)) + position).xyz;
    float3 originalWorldSpaceNormal = worldSpaceNormal.xyz;
    TerrainTriangleSampleResult sampledTerrain = SampleTerrain(terrainRelatedPosition.xz, _TerrainPosition, _TerrainSize, _HeightMapSize.x);

    float terrainHeight = sampledTerrain.Height + _TerrainPosition.y;

    float2 inTerrainPosition = terrainRelatedPosition.xz - _TerrainPosition.xz;
    TerrainSampleResult terrainResult = GetTerrainProperties(inTerrainPosition / _TerrainSize.xz);

    float blendingHeight = terrainHeight + _TerrainBlendAmount;
    float blendFactor = saturate(smoothstep(blendingHeight, blendingHeight + _TerrainBlendSmoothingLength, terrainRelatedPosition.y));

    float3 blendedNormal = UnpackUintToFloat4(properties.Normal).xyz * 2.0f - 1.0f;
    float4 tangent = ConstructTerrainTangent(blendedNormal, float3(0.0f, 0.0f, 1.0f));

    float3x3 tangentToWorld = GetTerrainTangentToWorldMatrix(tangent, blendedNormal);
    float3 terrainNormal = mul(tangentToWorld, normalize(terrainResult.Normal));

    worldSpaceNormal.xyz = normalize(lerp(terrainNormal, worldSpaceNormal.xyz, blendFactor));
    Color = lerp(terrainResult.Albedo.rgb, Color, blendFactor);
    Smoothness = lerp(terrainResult.Smoothness, Smoothness, blendFactor);
    AO = lerp(terrainResult.AO, AO, blendFactor);
    Metallic = lerp(terrainResult.Metallic, Metallic, blendFactor);
#endif
    
    Normal = normalize(mul(WorldToTangent, worldSpaceNormal.xyz).xyz);
}