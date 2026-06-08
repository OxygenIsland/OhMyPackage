using UnityEngine;

namespace OhMyPackage
{
    /// <summary>
    /// 渲染策略初始化配置，由 PointCloudRenderer 构建后传入 IPointCloudRenderStrategy.TryInitialize。
    /// 只包含跨策略共享的参数；Shader / ComputeShader 路径等策略内部细节由各策略类以 const 自行维护。
    /// </summary>
    public class PointCloudRenderConfig
    {
        /// <summary>是否启用视锥体剔除（仅 InstancedIndirect 策略生效）</summary>
        public bool EnableFrustumCulling { get; set; } = false;

        /// <summary>初始点大小（策略初始化时同步到材质）</summary>
        public float PointSize { get; set; } = 0.05f;

        /// <summary>MeshPoints 子 GameObject 的父节点（为 null 时挂载到场景根节点）</summary>
        public Transform Parent { get; set; }

        /// <summary>
        /// 视锥体剔除用 ComputeShader（FrustumCulling.compute）。
        /// 为 null 时即使 EnableFrustumCulling = true 也会自动禁用剔除。
        /// 在 PointCloudRenderer Inspector 中直接拖入赋值，无需放入 Resources 文件夹。
        /// </summary>
        public ComputeShader FrustumCullingCompute { get; set; }
    }
}
