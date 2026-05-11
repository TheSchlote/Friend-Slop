#define FIXED_POINT_SCALER 4.0f
#define FIXED_POINT_DECODE_SCALER 63.75f

#if !defined(SHARED_INCLUDED)
#define SHARED_INCLUDED

uint PackFloat4ToUint(float4 value)
{
    uint r = (uint)(saturate(value.x) * 255.0f + 0.5f);
    uint g = (uint)(saturate(value.y) * 255.0f + 0.5f);
    uint b = (uint)(saturate(value.z) * 255.0f + 0.5f);
    uint a = (uint)(saturate(value.w) * 255.0f + 0.5f);

    return (r) | (g << 8) | (b << 16) | (a << 24);
}

float4 UnpackUintToFloat4(uint packed)
{
    float r = (packed & 0xFF)        / 255.0f;
    float g = ((packed >> 8) & 0xFF) / 255.0f;
    float b = ((packed >> 16) & 0xFF) / 255.0f;
    float a = ((packed >> 24) & 0xFF) / 255.0f;

    return float4(r, g, b, a);
}

float4x4 ComputeView(float3 direction)
{
    float3 align = float3(0.0f, 1.0f, 0.0f);
    float3 right = -normalize(cross(direction, align));
    float3 up = -cross(right, direction);

    float4x4 basisMatrix = float4x4(
            right.x, up.x, direction.x, 0.0f,
            right.y, up.y, direction.y, 0.0f,
            right.z, up.z, direction.z, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f
        );

    return basisMatrix;
}

inline float2 OctahedronToUV(float3 direction)
{
    float3 octant = sign(direction);
    float sum = dot(direction, octant);        
    float3 octahedron = direction / sum;    

    if(octahedron.z < 0)
    {
        float3 absolute = abs(octahedron);
        octahedron.xy = octant.xy
                      * float2(1.0f - absolute.y, 1.0f - absolute.x);
    }

    return octahedron.xy * 0.5f + 0.5f;
}

float3 UVToOctahedron(float2 uv)
{
    float3 position = float3(2.0f * (uv - 0.5f), 0.0f);                

    float2 absolute = abs(position.xy);
    position.z = 1.0f - absolute.x - absolute.y;

    if(position.z < 0)
    {
        position.xy = sign(position.xy) 
                    * float2(1.0f - absolute.y, 1.0f - absolute.x);
    }

    return position;
}
#endif