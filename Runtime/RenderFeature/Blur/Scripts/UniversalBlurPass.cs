using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

class UniversalBlurPass : ScriptableRenderPass {
	private const string k_GlobalFullScreenBlurTexture = "_GlobalFullScreenBlurTexture";
	
	private static readonly int m_BlitTextureShaderID = Shader.PropertyToID("_BlitTexture");
	private static readonly int m_KawaseOffsetID = Shader.PropertyToID("_KawaseOffset");

	private PassData m_PassData;

	private RTHandle m_tmpRT1;
	private RTHandle m_tmpRT2;

	public void Setup(Action<PassData> passDataOptions, float downsample, in RenderingData renderingData) {
		m_PassData ??= new PassData();
		
		passDataOptions?.Invoke(m_PassData);

		RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
		rtDesc.depthBufferBits = (int) DepthBits.None;

		rtDesc.width  = Mathf.RoundToInt(rtDesc.width  / downsample);
		rtDesc.height = Mathf.RoundToInt(rtDesc.height / downsample);
		
		#if UNITY_2022_1_OR_NEWER
		RenderingUtils.ReAllocateHandleIfNeeded(ref m_tmpRT1, rtDesc, name: "_PassRT1");
		RenderingUtils.ReAllocateHandleIfNeeded(ref m_tmpRT2, rtDesc, name: "_PassRT2");
		#else
		RenderEmul_2021.ReAllocateIfNeeded(ref m_tmpRT1, rtDesc, name: "_PassRT1");
		RenderEmul_2021.ReAllocateIfNeeded(ref m_tmpRT2, rtDesc, name: "_PassRT2");
		#endif
		
		m_PassData.tmpRT1 = m_tmpRT1;
		m_PassData.tmpRT2 = m_tmpRT2;
	}

	public void Dispose() {
		m_tmpRT1?.Release();
		m_tmpRT2?.Release();
	}

	// ── URP 17 (RenderGraph) 路径 ─────────────────────────────────────────
	public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
		var resourceData = frameData.Get<UniversalResourceData>();
		var cameraData   = frameData.Get<UniversalCameraData>();

		if (m_PassData?.effectMaterial == null) return;
		if (cameraData.isPreviewCamera) return;
		if (cameraData.isSceneViewCamera && m_PassData.disableInSceneView) return;
		if (!m_PassData.requiresColor) return;

		var rt1Handle = renderGraph.ImportTexture(m_tmpRT1);
		var rt2Handle = renderGraph.ImportTexture(m_tmpRT2);

		using (var builder = renderGraph.AddUnsafePass<RGPassData>("KawaseBlur", out var rgData, m_PassData.profilingSampler)) {
			rgData.source         = resourceData.activeColorTexture;
			rgData.tmpRT1         = rt1Handle;
			rgData.tmpRT2         = rt2Handle;
			rgData.effectMaterial = m_PassData.effectMaterial;
			rgData.scale          = m_PassData.scale;
			rgData.iterations     = m_PassData.iterations;

			builder.UseTexture(rgData.source, AccessFlags.Read);
			builder.UseTexture(rt1Handle, AccessFlags.ReadWrite);
			builder.UseTexture(rt2Handle, AccessFlags.ReadWrite);
			builder.AllowPassCulling(false);
			builder.AllowGlobalStateModification(true);

			builder.SetRenderFunc(static (RGPassData data, UnsafeGraphContext ctx) => {
				var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);

				// Step1: 将相机颜色拷贝到 tmpRT1
				Blitter.BlitCameraTexture(cmd, data.source, data.tmpRT1);

				// Step2: Kawase 模糊迭代（shader 使用 _MainTex，用 cmd.Blit 设置）
				RTHandle rt1 = data.tmpRT1;
				RTHandle rt2 = data.tmpRT2;

				cmd.SetGlobalFloat(m_KawaseOffsetID, 1.5f);
				cmd.Blit(rt1, rt2, data.effectMaterial, 0);

				for (var i = 1; i <= data.iterations; i++) {
					cmd.SetGlobalFloat(m_KawaseOffsetID, 0.5f + i * data.scale);
					cmd.Blit(rt1, rt2, data.effectMaterial, 0);
					(rt1, rt2) = (rt2, rt1);
				}

				// Step3: 将模糊结果暴露给 UI Shader 采样
				cmd.SetGlobalTexture(k_GlobalFullScreenBlurTexture, rt2);
			});
		}
	}

	// ── Legacy 兼容路径（仅在 URP_COMPATIBILITY_MODE 下有效）────────────────
	[Obsolete]
	public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
		ExecutePass(m_PassData, ref renderingData, ref context);
	}

	private static void ExecutePass(PassData passData, ref RenderingData renderingData,
	                                ref ScriptableRenderContext context) 
	{
		var passMaterial = passData.effectMaterial;
		var tmpRT1       = passData.tmpRT1;
		var tmpRT2       = passData.tmpRT2;
		var scale    = passData.scale;

		// should not happen as we check it in feature
		if (passMaterial == null)
			return;

		if (renderingData.cameraData.isPreviewCamera)
			return;

		// if is scene camera and we want to disable in scene view
		if (renderingData.cameraData.isSceneViewCamera && passData.disableInSceneView)
			return;

		CommandBuffer cmd = CommandBufferPool.Get();
			
		var cameraData = renderingData.cameraData;

		using (new ProfilingScope(cmd, passData.profilingSampler)) {
			ProcessEffect(ref context);
		}


		void ProcessEffect(ref ScriptableRenderContext context) {
			if (passData.requiresColor) {
				#pragma warning disable CS0618
				var source = cameraData.renderer.cameraColorTargetHandle;
				#pragma warning restore CS0618
				
				// --- Start
				#if UNITY_2022_1_OR_NEWER
				Blitter.BlitCameraTexture(cmd, source, tmpRT1);
				#else
				cmd.Blit(source, tmpRT1);
				#endif
				
				void DoBlit () {
					cmd.Blit(tmpRT1, tmpRT2, passMaterial, 0);
					// Blitter.BlitCameraTexture(cmd, tmpRT1, tmpRT2, passMaterial, 0);
				}
				
				{
					cmd.SetGlobalFloat(m_KawaseOffsetID, 1.5f);
					DoBlit();
				
					for (var i = 1; i <= passData.iterations; i++) {
						cmd.SetGlobalFloat(m_KawaseOffsetID, 0.5f + i * scale);
						DoBlit();
						
						(tmpRT1, tmpRT2) = (tmpRT2, tmpRT1);
					}
				}
				
				cmd.SetGlobalTexture(k_GlobalFullScreenBlurTexture, tmpRT2);
				// --- End
			}
			
			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);
		}
	}

	internal class RGPassData {
		internal TextureHandle source;
		internal TextureHandle tmpRT1;
		internal TextureHandle tmpRT2;
		internal Material effectMaterial;
		internal float scale;
		internal int iterations;
	}

	internal class PassData {
		internal Material effectMaterial;
		internal int passIndex;
		internal bool requiresColor;
		internal bool disableInSceneView;
		internal bool isBeforeTransparents;
		
		public ProfilingSampler profilingSampler;

		public float scale;
		public int iterations;
		
		public RTHandle tmpRT1;
		public RTHandle tmpRT2;
	}
}