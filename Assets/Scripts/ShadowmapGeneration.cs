using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


public class ShadowmapGenerator : ScriptableRendererFeature
{
    class CustomRenderPass : ScriptableRenderPass
    {
        // 光源参数
        private Vector3 m_LightDir = Vector3.down;
        private Vector3 m_LightPos = Vector3.zero;
        private float m_NearPlane = 0.1f;
        private float m_FarPlane = 100f;
        private float m_OrthoSize = 50f;

        private RTHandle m_ShadowmapTexHandle; // Shadowmap指针
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

        // Here you can implement the rendering logic.
        // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
        // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
        // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
        // 手动计算View矩阵
        private Matrix4x4 CalculateLightViewMatrix()
        {
            Vector3 lightDir = m_LightDir.normalized;
            Vector3 lightUp = Vector3.Dot(lightDir, Vector3.up) > 0.99f ? Vector3.forward : Vector3.up;
            return Matrix4x4.TRS(m_LightPos, Quaternion.LookRotation(lightDir, lightUp), Vector3.one).inverse;
        }

        // 手动计算Projection矩阵
        private Matrix4x4 CalculateLightProjectionMatrix()
        {
            float halfSize = m_OrthoSize * 0.5f;
            Matrix4x4 proj = Matrix4x4.Ortho(-halfSize, halfSize, -halfSize, halfSize, m_NearPlane, m_FarPlane);

            // 处理反转ZBuffer
            if (SystemInfo.usesReversedZBuffer)
            {
                proj[2, 2] = -1.0f;
                proj[2, 3] = -m_NearPlane;
            }

            // 处理UV起点
            if (SystemInfo.graphicsUVStartsAtTop)
            {
                proj[1, 1] = -proj[1, 1];
            }

            return proj;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 计算光源VP矩阵
            Matrix4x4 viewMatrix = CalculateLightViewMatrix();
            Matrix4x4 projMatrix = CalculateLightProjectionMatrix();
            Matrix4x4 vpMatrix = projMatrix * viewMatrix;

            CommandBuffer cmd = CommandBufferPool.Get("Shadowmap Generation");

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
            // k_ShadowmapTexName 应该是 "_ShadowMap"
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
        renderer.EnqueuePass(m_ShadowmapRenderPass);
    }
}
