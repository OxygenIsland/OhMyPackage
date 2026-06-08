using UnityEngine;

namespace OhMyPackage
{
    /// <summary>
    /// 点云渲染策略接口。
    /// 每个实现对应一种 GPU 渲染路径，负责管理该路径下的全部 GPU 资源。
    ///
    /// 数据所有权在 PointCloudRenderer（协调者），策略只在 RebuildBuffers 时消费数据，
    /// 不缓存 positions/colors 引用。
    /// </summary>
    public interface IPointCloudRenderStrategy
    {
        /// <summary>策略名称（仅用于日志/调试）</summary>
        string Name { get; }

        /// <summary>
        /// 是否启用视锥体剔除。
        /// 不支持剔除的策略应将 setter 实现为 no-op，getter 固定返回 false。
        /// </summary>
        bool EnableFrustumCulling { get; set; }

        /// <summary>
        /// 初始化策略所需的 GPU 资源。
        /// 返回 false 表示当前平台/Shader 条件不满足，调用方应尝试下一个候选策略。
        /// </summary>
        bool TryInitialize(PointCloudRenderConfig config);

        /// <summary>
        /// 点云数据或点大小发生变化时重建 GPU Buffer。
        /// 此方法可能涉及较大 CPU/GPU 数据传输，应在数据实际变化时才调用，而非每帧调用。
        /// </summary>
        void RebuildBuffers(Vector3[] positions, Color32[] colors, int pointCount, float pointSize);

        /// <summary>
        /// 每帧调用，提交本帧的绘制命令。
        /// </summary>
        /// <param name="bounds">包围盒，用于摄像机可见性判断</param>
        /// <param name="camera">渲染/剔除相机</param>
        /// <param name="layer">目标渲染 Layer</param>
        void Draw(Bounds bounds, Camera camera, int layer);

        /// <summary>释放所有 GPU 资源（ComputeBuffer、Material、子 GameObject 等）</summary>
        void Release();
    }
}
