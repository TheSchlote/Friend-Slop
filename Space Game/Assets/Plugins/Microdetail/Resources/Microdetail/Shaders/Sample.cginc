#include "Common.cginc"

sampler3D _SDF;
sampler3D _Albedo;
sampler3D _Mask;
float3 _TextureSize;
int _MaxIterationsCount;
float _MipShift;

inline float SampleSDF(float4 uvwl)
{
    uvwl.w += _MipShift;
    return tex3Dlod(_SDF, uvwl).r;
}

inline float GetValueScaler()
{
    return FIXED_POINT_DECODE_SCALER;
}

inline void Sample(float4 uvwl, out float3 albedo, out float metallic, out float smoothness, out float ao)
{
    uvwl.w += _MipShift;
    float4 value = tex3Dlod(_Albedo, uvwl);
    float4 mask = tex3Dlod(_Mask, uvwl);
    
    albedo = value.rgb;
    metallic = mask.r;
    ao = mask.g;
    smoothness = mask.a;
}

inline float3 GetVolumeSize()
{
    return _TextureSize.xyz;
}

inline float GetThickness(float lodFactor)
{
    return 1.0f;
}

inline float GetMinHitDistance()
{
    return 0.1f;
}

inline float GetStepSize(float sdf, float lodFactor)
{
    if (sdf < 0.0f)
        return sdf;
    
    return max(2.0f, sdf);
}

inline int GetMaxIterationsCount()
{
#if MICRODETAIL_PREVIEW
    return 1000;
#else
    return _MaxIterationsCount;
#endif
}