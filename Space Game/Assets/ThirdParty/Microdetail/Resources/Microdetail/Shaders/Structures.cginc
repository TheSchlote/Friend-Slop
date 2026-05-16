#ifndef MICRODETAIL_DATA_STRUCTURES
#define MICRODETAIL_DATA_STRUCTURES

struct IntersectionResult
{
    bool Hit;
    float3 Position;
    float3 Normal;
};

struct WindOctaveParameters
{
    float Strength;
    float Frequency;
    float Amplitude;
};

struct Ray
{
    float3 origin;
    float3 direction;
};

struct Transformation
{
    float4x4 Transformation;
};

struct MicrodetailProperties
{
    uint Tint;
    float Lod;
#if defined(MICRODETAIL_TERRAIN_BLENDING)
    uint Normal;
#endif
};

struct FrustumPlane
{
    float3 Position;
    float3 Normal;
};
    
struct ViewParameters
{
    FrustumPlane Planes[6];
    float4x4 WorldToScreenMatrix;
    float4x4 CameraToWorld;
    float2 ScreenSize;
};

#endif