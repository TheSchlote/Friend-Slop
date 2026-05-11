inline IntersectionResult FindIntersection(Ray ray, float lod, float lodFactor, float normalSpherization)
{
    IntersectionResult result;
    result.Hit = false;
    result.Position = 0.0f;
    result.Normal = 0.0f;

    float3 position = ray.origin * GetVolumeSize();
    
    int iteration = 1;
    float3 inverseTextureSize = 1.0f / GetVolumeSize();
    ray.direction *= inverseTextureSize;
    ray.direction = normalize(ray.direction);

    float thickness = GetThickness(lodFactor);

    float minHitDistance = GetMinHitDistance();
    float3 lastPositivePosition = ray.origin;
    float lastPositiveSampled = 0.0f;
    float step = 1.0f;
    for (; iteration <= GetMaxIterationsCount(); iteration++)
    {
        float sampled = SampleSDF(float4(position * inverseTextureSize, lod)) * GetValueScaler();
        if (sampled > 0.0f)
        {
            lastPositivePosition = position;
            lastPositiveSampled = sampled;
        }
        else
        {
            step *= 0.5f;
            position = lastPositivePosition;
            sampled = lastPositiveSampled;
        }
        
        float currentSDF = sampled - thickness;
        position += ray.direction * GetStepSize(currentSDF, lodFactor) * step;
        if (currentSDF <= 0.0f || iteration == GetMaxIterationsCount())
        {
            result.Hit = currentSDF < minHitDistance;
            break;
        }

        if (any(position < 0.0f) || any(position > GetVolumeSize()))
            break;
    }

    if (result.Hit)
    {
        result.Position = position * inverseTextureSize;
        
        float3 shift = inverseTextureSize * 1.0f;
        float3 sphericalNormal = normalize(position - 0.5f * GetVolumeSize());
        float3 difference = float3(
                SampleSDF(float4(result.Position.x + shift.x, result.Position.y, result.Position.z, lod)) - 
                SampleSDF(float4(result.Position.x - shift.x, result.Position.y, result.Position.z, lod)),
                SampleSDF(float4(result.Position.x, result.Position.y + shift.y, result.Position.z, lod)) - 
                SampleSDF(float4(result.Position.x, result.Position.y - shift.y, result.Position.z, lod)),
                SampleSDF(float4(result.Position.x, result.Position.y, result.Position.z + shift.z, lod)) - 
                SampleSDF(float4(result.Position.x, result.Position.y, result.Position.z - shift.z, lod))
            );

        float3 normalUnscaled = difference * GetValueScaler();

        result.Normal = normalize(lerp(normalUnscaled, sphericalNormal, max(normalSpherization, 0.01f)));
    }
    else
       discard;

    return result;
}