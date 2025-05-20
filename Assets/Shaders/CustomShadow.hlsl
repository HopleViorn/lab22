#define CUSTOM_SHADOW_BIAS -0.007
#define PCSS_SAMPLE_COUNT 16
#define PCSS_SEARCH_RADIUS 0.01

TEXTURE2D(_ShadowMap);
float4x4 _LightVPMatrix;
float _LightSize = 0.04;

// 泊松圆盘采样点
static const float2 PoissonDisk[16] = {
    float2(-0.94201624, -0.39906216),
    float2(0.94558609, -0.76890725),
    float2(-0.094184101, -0.92938870),
    float2(0.34495938, 0.29387760),
    float2(-0.91588581, 0.45771432),
    float2(-0.81544232, -0.87912464),
    float2(-0.38277543, 0.27676845),
    float2(0.97484398, 0.75648379),
    float2(0.44323325, -0.97511554),
    float2(0.53742981, -0.47373420),
    float2(-0.26496911, -0.41893023),
    float2(0.79197514, 0.19090188),
    float2(-0.24188840, 0.99706507),
    float2(0.13912018, -0.78666421),
    float2(0.23198142, -0.36273035),
    float2(-0.81409955, 0.91437590)
};

float FindBlockerDistance(float2 uv, float zReceiver)
{
    float blockerSum = 0.0;
    int blockerCount = 0;
    
    for (int i = 0; i < PCSS_SAMPLE_COUNT; i++)
    {
        float2 sampleUV = uv + PoissonDisk[i] * PCSS_SEARCH_RADIUS;
        float sampleDepth = SAMPLE_TEXTURE2D(_ShadowMap, sampler_LinearClamp, sampleUV).r;
        
        #if UNITY_REVERSED_Z
        if (zReceiver > sampleDepth + CUSTOM_SHADOW_BIAS)
        #else
        if (zReceiver < sampleDepth + CUSTOM_SHADOW_BIAS)
        #endif
        {
            blockerSum += sampleDepth;
            blockerCount++;
        }
    }
    
    return blockerCount > 0 ? blockerSum / blockerCount : -1.0;
}

real CustomShadow(float3 positionWS)
{
    float4 shadowCoord = mul(_LightVPMatrix, float4(positionWS, 1.0));
    
    shadowCoord.xyz /= shadowCoord.w;
    
    float2 uv = shadowCoord.xy * 0.5 + 0.5;
    
    #if UNITY_UV_STARTS_AT_TOP
        uv.y = 1.0 - uv.y;
    #endif
    
    if (any(abs(shadowCoord.xy) > 1.0 || shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0))
        return 1.0;
    
    float zReceiver = shadowCoord.z;
    float avgBlockerDepth = FindBlockerDistance(uv, zReceiver);
    
    // 没有遮挡物时返回完全光照
    if (avgBlockerDepth < 0.0)
        return 0.0;
        
    // 计算半影大小
    float penumbraSize = (zReceiver - avgBlockerDepth) * _LightSize / avgBlockerDepth;
    penumbraSize = clamp(penumbraSize, 0.001, PCSS_SEARCH_RADIUS * 2.0);
    
    // PCSS软阴影计算
    float shadow = 0.0;
    for (int i = 0; i < PCSS_SAMPLE_COUNT; i++)
    {
        float2 sampleUV = uv + PoissonDisk[i] * penumbraSize;
        float sampleDepth = SAMPLE_TEXTURE2D(_ShadowMap, sampler_LinearClamp, sampleUV).r;

        #if UNITY_REVERSED_Z
        shadow += zReceiver <= sampleDepth + CUSTOM_SHADOW_BIAS ? 0.0 : 1.0;
        #else
        shadow += zReceiver >= sampleDepth + CUSTOM_SHADOW_BIAS ? 0.0 : 1.0;
        #endif
    }
    
    return shadow / PCSS_SAMPLE_COUNT;
}