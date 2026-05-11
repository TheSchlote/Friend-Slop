float hash11(float p)
{
    p = frac(p * .1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float hash12(float2 p)
{
	float3 p3  = frac(float3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float hash13(float3 p3)
{
	p3  = frac(p3 * .1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

float hash14(float4 p4)
{
	p4 = frac(p4 * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.x + p4.y) * (p4.z + p4.w));
}

float2 hash21(float p)
{
	float3 p3 = frac((float3)p * float3(.1031, .1030, .0973));
	p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);

}

float2 hash22(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float2 hash23(float3 p3)
{
	p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float3 hash31(float p)
{
   float3 p3 = frac((float3)p * float3(.1031, .1030, .0973));
   p3 += dot(p3, p3.yzx + 33.33);
   return frac((p3.xxy + p3.yzz) * p3.zyx); 
}


float3 hash32(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yzz) * p3.zyx);
}

float3 hash33(float3 p3)
{
	p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return frac((p3.xxy + p3.yxx) * p3.zyx);
}

float4 hash41(float p)
{
	float4 p4 = frac((float4)p * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 hash42(float2 p)
{
	float4 p4 = frac(float4(p.xyxy) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 hash43(float3 p)
{
	float4 p4 = frac(float4(p.xyzx) * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float4 hash44(float4 p4)
{
	p4 = frac(p4 * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy + 33.33);
    return frac((p4.xxyz + p4.yzzw) * p4.zywx);
}

float fade(float t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float2 grad(int hash, float2 p)
{
    int h = hash & 7;
    float u = h < 4 ? p.x : p.y;
    float v = h < 4 ? p.y : p.x;
    return (float2(u, v) * (h & 1 ? -1.0 : 1.0));
}

float PerlinNoise2D(float2 pos)
{
    float2 i = floor(pos);
    float2 f = frac(pos);

    float2 u = float2(fade(f.x), fade(f.y));

    int a = int(i.x + i.y * 57.0) & 255;
    int b = int(i.x + 1.0 + i.y * 57.0) & 255;
    int c = int(i.x + (i.y + 1.0) * 57.0) & 255;
    int d = int(i.x + 1.0 + (i.y + 1.0) * 57.0) & 255;

    float2 g0 = grad(a, f - float2(0.0, 0.0));
    float2 g1 = grad(b, f - float2(1.0, 0.0));
    float2 g2 = grad(c, f - float2(0.0, 1.0));
    float2 g3 = grad(d, f - float2(1.0, 1.0));

    float lerpX0 = lerp(dot(g0, f), dot(g1, f - float2(1.0, 0.0)), u.x);
    float lerpX1 = lerp(dot(g2, f - float2(0.0, 1.0)), dot(g3, f - float2(1.0, 1.0)), u.x);
    return lerp(lerpX0, lerpX1, u.y);
}