#ifndef MICRODETAIL_TERRAIN
#define MICRODETAIL_TERRAIN

struct TriangleHitResult
{
    float3 Position;
    float Distance;
    float2 UV;
};

TriangleHitResult GetClosestPointOnTriangle(float3 targetPoint, float3 a, float3 b, float3 c, float2 uvA, float2 uvB, float2 uvC, float3 normal)
{
    float3 ab = b - a;
    float3 ac = c - a;
    
    float d = dot(normal, targetPoint - a);
    float3 Q = targetPoint - d * normal;

    float3 v0 = ab;
    float3 v1 = ac;
    float3 v2 = Q - a;

    float d00 = dot(v0, v0);
    float d01 = dot(v0, v1);
    float d11 = dot(v1, v1);
    float d20 = dot(v2, v0);
    float d21 = dot(v2, v1);
    float denominator = d00 * d11 - d01 * d01;

    float v = (d11 * d20 - d01 * d21) / denominator;
    float w = (d00 * d21 - d01 * d20) / denominator;
    float u = 1.0f - v - w;

    TriangleHitResult result;
    result.Position = all(float3(u, v, w) >= 0) ? Q : 100000.0f;
    result.Distance = length(targetPoint - result.Position);
    result.UV = u * uvA + v * uvB + w * uvC;

    return result;
}

struct TerrainTriangleSampleResult
{
    float3 Normal;
    float Height;
    float2 ChunkUV;
};

float3 CalculateNormal(Texture2D<float4> heightmap, int2 position, float2 terrainResolution, float3 terrainSize)
{
    float texelSizeX = terrainSize.x / terrainResolution.x;
    float texelSizeZ = terrainSize.z / terrainResolution.y;

    int2 maxIndex = terrainResolution - 1;

    int previousX = clamp(position.x - 1, 0, maxIndex.x);
    int nextX = clamp(position.x + 1, 0, maxIndex.x);
    int previousY = clamp(position.y - 1, 0, maxIndex.y);
    int nextY = clamp(position.y + 1, 0, maxIndex.y);
    
    float hL = heightmap[int2(previousX, position.y)].r;
    float hR = heightmap[int2(nextX, position.y)].r;
    float hD = heightmap[int2(position.x, previousY)].r;
    float hU = heightmap[int2(position.x, nextY)].r;

    float dX = (hR - hL) / (2.0f * texelSizeX);
    float dZ = (hU - hD) / (2.0f * texelSizeZ);
    float dY = 1.0f;

    return normalize(float3(-dX, dY, -dZ));
}

float GetOddNegativeScaleValue()
{
    return unity_WorldTransformParams.w >= 0.0 ? 1.0 : -1.0;
}

float3x3 CreateTangentToWorldMatrix(float3 normal, float3 tangent, float flipSign)
{
    float sgn = flipSign * GetOddNegativeScaleValue();
    float3 bitangent = cross(normal, tangent) * sgn;

    return float3x3(tangent, bitangent, normal);
}

float3x3 BuildTangentToWorldMatrix(float4 tangentWS, float3 normalWS)
{
    float3 unnormalizedNormalWS = normalWS;
    float renormFactor = 1.0 / max(1.175494351e-38, length(unnormalizedNormalWS));
    float3x3 tangentToWorld = CreateTangentToWorldMatrix(unnormalizedNormalWS, tangentWS.xyz, tangentWS.w > 0.0 ? 1.0 : -1.0);

    tangentToWorld[0] = tangentToWorld[0] * renormFactor;
    tangentToWorld[1] = tangentToWorld[1] * renormFactor;
    tangentToWorld[2] = tangentToWorld[2] * renormFactor;

    return tangentToWorld;
}

float3x3 GetTerrainTangentToWorldMatrix(float4 tangent, float3 normal)
{
    return BuildTangentToWorldMatrix(tangent, normal);
}

float4 ConstructTerrainTangent(float3 normal, float3 positiveZ)
{
    float3 tangent = cross(normal, positiveZ);
    return float4(tangent, -1);
}

inline TerrainTriangleSampleResult SampleTerrain(float2 worldPosition, float3 terrainPosition, float3 terrainSize, float heightmapSize)
{
    TerrainTriangleSampleResult result;
    worldPosition -= terrainPosition.xz;
    float2 terrainTextureSize = heightmapSize;
    float2 invertedTextureSize = 1.0f / terrainTextureSize;
    float2 uv = worldPosition / terrainSize.xz;

    float2 pixelSpaceUV = uv * (terrainTextureSize - 1);
    float2 fractional = frac(pixelSpaceUV);
    float2 chunkUV = floor(pixelSpaceUV);

    float2 baseHeightUV = chunkUV / terrainTextureSize;
    float2 diagonalHeightUV = baseHeightUV + invertedTextureSize;

    float2 direction = fractional.x > fractional.y ? 
        float2(1.0f, 0.0f) :
        float2(0.0f, 1.0f);
        
    float2 secondDirection = fractional.x <= fractional.y ? 
        float2(1.0f, 0.0f) :
        float2(0.0f, 1.0f);

    float2 sideHeightUV = baseHeightUV + direction * invertedTextureSize;

    float baseHeight = SampleHeightTexture(baseHeightUV).r;
    float diagonalHeight = SampleHeightTexture(diagonalHeightUV).r;
    float sideHeight = SampleHeightTexture(sideHeightUV).r;

    float difference = diagonalHeight - sideHeight;
    float firstHeight = lerp(baseHeight, sideHeight, dot(fractional, direction));
    float secondHeight = difference + firstHeight;
    float finalHeight = lerp(firstHeight, secondHeight, dot(fractional, secondDirection));

    float3 scale = float3(terrainSize.x * invertedTextureSize.x, terrainSize.y, terrainSize.z * invertedTextureSize.y);

    float3 basePosition = float3(0.0f, baseHeight, 0.0f) * scale;
    float3 firstBase = float3(direction.x, sideHeight, direction.y) * scale;
    float3 secondBase = float3(1.0f, diagonalHeight, 1.0f) * scale;

    float3 differenceA = firstBase - basePosition;
    float3 differenceB = secondBase - basePosition;

    float3 normal = normalize(cross(differenceA, differenceB));
    if (normal.y < 0.0f)
        normal = -normal;

    result.Normal = normal;
    result.Height = finalHeight * terrainSize.y;
    result.ChunkUV = chunkUV;

    return result;
}
#endif