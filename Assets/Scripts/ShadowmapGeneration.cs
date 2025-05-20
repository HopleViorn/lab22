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
        private const string k_ShadowmapTexName = "_ShadowMap"; // Shadowmap在Shader中的引用名


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
        
        private Matrix4x4 CalculateLightViewMatrix()
        {
            Vector3 lightDir = m_LightDir.normalized;
            // 使用 Mathf.Abs 来确保在 lightDir 与 Vector3.up 近似平行或反向平行时都能正确选择辅助 up 向量
            Vector3 up = Mathf.Abs(Vector3.Dot(lightDir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;

            // 计算光源坐标系的三个正交轴
            // viewRight 对应光源坐标系的 X 轴
            Vector3 viewRight = Vector3.Cross(up, lightDir).normalized;
            // viewUp 对应光源坐标系的 Y 轴
            // 由于 lightDir 和 viewRight 已经是正交且归一化的，它们的叉积结果 viewUp 也会是归一化的
            Vector3 viewUp = Vector3.Cross(lightDir, viewRight); 

            // 初始化视图矩阵 (Matrix4x4 默认构造函数创建的是单位矩阵)
            Matrix4x4 viewMatrix = new Matrix4x4();

            // 设置矩阵的行
            // 第0行：X轴基向量 和 X轴相关的平移
            viewMatrix[0,0] = viewRight.x;
            viewMatrix[0,1] = viewRight.y;
            viewMatrix[0,2] = viewRight.z;
            viewMatrix[0,3] = -Vector3.Dot(viewRight, m_LightPos);

            // 第1行：Y轴基向量 和 Y轴相关的平移
            viewMatrix[1,0] = viewUp.x;
            viewMatrix[1,1] = viewUp.y;
            viewMatrix[1,2] = viewUp.z;
            viewMatrix[1,3] = -Vector3.Dot(viewUp, m_LightPos);

            // 第2行：Z轴基向量（即光源的观察方向）和 Z轴相关的平移
            viewMatrix[2,0] = lightDir.x;
            viewMatrix[2,1] = lightDir.y;
            viewMatrix[2,2] = lightDir.z;
            viewMatrix[2,3] = -Vector3.Dot(lightDir, m_LightPos);

            // 第3行：标准齐次坐标行
            viewMatrix[3,0] = 0.0f;
            viewMatrix[3,1] = 0.0f;
            viewMatrix[3,2] = 0.0f;
            viewMatrix[3,3] = 1.0f;
            
            return viewMatrix;
        }
        
        private Matrix4x4 CalculateLightProjectionMatrix()
        {
            Matrix4x4 projectionMatrix = Matrix4x4.Ortho(-m_OrthoSize, m_OrthoSize, -m_OrthoSize, m_OrthoSize, m_NearPlane, m_FarPlane);
            return projectionMatrix;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 计算光源的VP矩阵
            Matrix4x4 viewMatrix = CalculateLightViewMatrix();
            Matrix4x4 projMatrix = CalculateLightProjectionMatrix();
            Matrix4x4 vpMatrix = GL.GetGPUProjectionMatrix(projMatrix, false) * viewMatrix; // 注意：Unity 在某些平台上需要这个转换

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
            Vector3 lightDir = -mainLight.light.transform.forward;
            Vector3 lightPos = mainLight.light.transform.position;
            
            // 设置光源参数
            m_ShadowmapRenderPass.SetLightParameters(
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


