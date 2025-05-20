TEXTURE2D(_ShadowMap);
 float4x4 _LightVPMatrix;
 real CustomShadow(float3 positionWS)
 {
     // 转换到光源裁剪空间
     float4 shadowCoord = mul(_LightVPMatrix, float4(positionWS, 1.0));
     
     // 透视除法得到NDC坐标 [-1,1]
     shadowCoord.xyz /= shadowCoord.w;
     
     // 转换到UV空间 [0,1]
     float2 uv = shadowCoord.xy * 0.5 + 0.5;
     
     // 处理平台差异的UV方向
     #if UNITY_UV_STARTS_AT_TOP
         uv.y = 1.0 - uv.y;
     #endif
     
     // 检查是否在裁剪空间内
     if (any(abs(shadowCoord.xy) > 1.0 || shadowCoord.z <= 0.0 || shadowCoord.z >= 1.0))
         return 1.0; // 不在阴影贴图范围内，视为无阴影
     
     // 采样阴影贴图
     float shadowDepth = SAMPLE_TEXTURE2D(_ShadowMap, sampler_LinearClamp, uv).r;
     
     // 比较当前深度与阴影贴图深度
     #if UNITY_REVERSED_Z
         return shadowCoord.z <= shadowDepth ? 1.0 : 0.0;
     #else
         return shadowCoord.z >= shadowDepth ? 1.0 : 0.0;
     #endif
 }