
#define CUSTOM_SHADOW_BIAS -0.007
TEXTURE2D(_ShadowMap);
 float4x4 _LightVPMatrix;
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
     
     float shadowDepth = SAMPLE_TEXTURE2D(_ShadowMap, sampler_LinearClamp, uv).r;
     
     #if UNITY_REVERSED_Z
         return shadowCoord.z <= shadowDepth + CUSTOM_SHADOW_BIAS ? 0.0 : 1.0;
     #else
         return shadowCoord.z >= shadowDepth + CUSTOM_SHADOW_BIAS ? 0.0 : 1.0;
     #endif
 }