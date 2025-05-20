using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public class ShadowmapGenerator : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        // 光源参数
        private Vector3 m_LightDir;
        private Vector3 m_LightPos;
        private float m_NearPlane;
        private float m_FarPlane;
        private float m_OrthoSize;

        private RTHandle m_ShadowmapTexHandle; // Shadowmap指针
        private const string shadowKeyWord = "_CUSTOM_SHADOW_ON";


        public void SetLightParameters(Vector3 lightDir, Vector3 lightPos, float nearPlane, float farPlane, float orthoSize)
        {
            m_LightDir = lightDir;
            m_LightPos = lightPos;
            m_NearPlane = nearPlane;
            m_FarPlane = farPlane;
            m_OrthoSize = orthoSize;
        }
        public void SetRTHandles(ref RTHandle tex)
        {
        m_ShadowmapTexHandle = tex;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescripor)
        {
            ConfigureTarget(m_ShadowmapTexHandle);
            ConfigureClear(ClearFlag.Depth, Color.clear);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        // 计算光源视图矩阵
        private Matrix4x4 CalculateLightViewMatrix()
        {
            // 光源看向的方向
            Vector3 lookAt = m_LightPos + m_LightDir;
            
            // 创建视图矩阵 (光源空间)
            return Matrix4x4.LookAt(
                m_LightPos,    // 相机位置 (光源位置)
                lookAt,       // 看向的点
                Vector3.up    // 上方向 (使用世界空间上方向)
            );

        }

        // 计算光源投影矩阵
        private Matrix4x4 CalculateLightProjectionMatrix()
        {
            // 创建正交投影矩阵
            float halfSize = m_OrthoSize;
            
            // 处理反向Z缓冲区
            float near = m_NearPlane;
            float far = m_FarPlane;
            if (SystemInfo.usesReversedZBuffer)
            {
                // 反转近远平面值
                near = m_FarPlane;
                far = m_NearPlane;
            }
            
            Matrix4x4 projMatrix = Matrix4x4.Ortho(
                -halfSize, halfSize,  // 左右边界
                -halfSize, halfSize,  // 上下边界
                near,                 // 近平面 (已考虑反向Z)
                far                   // 远平面 (已考虑反向Z)
            );

            // 使用 GL.GetGPUProjectionMatrix 来确保投影矩阵适用于当前图形API和渲染到纹理的约定。
            // 这会处理Y轴翻转 (当 SystemInfo.graphicsUVStartsAtTop 为 true 且渲染到纹理时，
            // 例如在DirectX上) 和Z深度范围等问题。
            // 'true' 参数表示我们正在渲染到纹理 (此例中是 Shadowmap)。
            // projMatrix = GL.GetGPUProjectionMatrix(projMatrix, true);
            
            return projMatrix;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 计算光源VP矩阵
            Matrix4x4 viewMatrix = CalculateLightViewMatrix();
            Matrix4x4 projMatrix = CalculateLightProjectionMatrix();
            Matrix4x4 vpMatrix = projMatrix * viewMatrix;




            // 调试VP矩阵
            // Debug.Log($"View Matrix:\n{viewMatrix}");
            // Debug.Log($"Projection Matrix:\n{projMatrix}");
            // Debug.Log($"VP Matrix:\n{vpMatrix}");
            
            // // 检查光源参数
            // Debug.Log($"Light Position: {m_LightPos}, Light Direction: {m_LightDir}");
            // Debug.Log($"Ortho Size: {m_OrthoSize}, Near: {m_NearPlane}, Far: {m_FarPlane}");


            CommandBuffer cmd = CommandBufferPool.Get("Shadowmap Generation");
            
            cmd.EnableShaderKeyword(shadowKeyWord);

            // 1. 设置渲染目标为我们的阴影贴图
            cmd.SetRenderTarget(m_ShadowmapTexHandle);
            // 2. 清除渲染目标 (深度和可选的颜色)
            // 对于 Shadowmap，通常只需要清除深度。如果 RenderTarget 碰巧也有颜色缓冲，也一并清除。
            cmd.ClearRenderTarget(true, true, Color.clear); // true for depth, true for color

            // 3. 设置光源的VP矩阵，以便 CustomShadowCaster Pass 使用
            cmd.SetGlobalMatrix("_LightVPMatrix", vpMatrix);

            // 4. 执行命令缓冲，确保渲染目标和矩阵已为 DrawRenderers 设置好
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear(); // 清空命令缓冲，为后续可能的命令做准备或只是好习惯

            // 5. 定义绘制设置，使用 "CustomShadowCaster" LightMode
            var drawSettings = new DrawingSettings(new ShaderTagId("CustomShadowCaster"),
                new SortingSettings(renderingData.cameraData.camera) { criteria = SortingCriteria.CommonOpaque });
            var filterSettings = new FilteringSettings(RenderQueueRange.opaque);

            // 6. 绘制场景中符合条件的物体到当前渲染目标 (即 m_ShadowmapTexHandle)
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);

            // 7. 将生成的阴影图设置为全局纹理，以便其他Shader可以访问
            cmd.SetGlobalTexture(k_ShadowmapTexName, m_ShadowmapTexHandle);
            context.ExecuteCommandBuffer(cmd); // 执行设置全局纹理的命令
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }


        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
    }

    CustomRenderPass m_ShadowmapRenderPass;

    private RTHandle m_ShadowmapTexHandle; // Shadowmap指针
    private const string k_ShadowmapTexName = "_ShadowMap"; // Shadowmap在Shader中的引用名
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData){
        var desc = new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.Shadowmap, 24);
    RenderingUtils.ReAllocateIfNeeded(ref m_ShadowmapTexHandle, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: k_ShadowmapTexName);
    //将Shadowmap传入RenderPass
    m_ShadowmapRenderPass.SetRTHandles(ref m_ShadowmapTexHandle);
    }

    /// <inheritdoc/>
    public override void Create()
    {
        m_ShadowmapRenderPass = new CustomRenderPass();

        // Configures where the render pass should be injected.
        m_ShadowmapRenderPass.renderPassEvent = RenderPassEvent.BeforeRenderingShadows;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 获取主光源信息
        if (renderingData.lightData.mainLightIndex >= 0)
        {
            var mainLight = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex];
            Vector3 lightDir = mainLight.localToWorldMatrix.GetColumn(2);
            Vector3 lightPos = mainLight.localToWorldMatrix.GetColumn(3);
            
            // 设置光源参数
            m_ShadowmapRenderPass.SetLightParameters(
                // Vector3.down,
                // new Vector3(0,-0.5f,0),
                lightDir,
                lightPos,
                0.01f,  // nearPlane
                15f,    // farPlane
                6f      // orthoSize
            );
        }

        renderer.EnqueuePass(m_ShadowmapRenderPass);
    }
}


