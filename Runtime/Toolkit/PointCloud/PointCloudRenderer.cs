/**
 * ==========================================
 * Author：albert
 * CreatTime：20250618
 * Description：点云渲染器（协调者）
 *
 * 职责（仅限以下内容，不含任何 GPU/Shader 操作）：
 *   1. 持有点云数据（positions / colors 数组）
 *   2. 计算 AABB Bounds
 *   3. 根据平台能力按优先级选择渲染策略（IPointCloudRenderStrategy）
 *   4. 在数据变化时通知策略重建 Buffer，每帧驱动策略执行绘制
 *
 * 渲染策略候选（按优先级）：
 *   1. InstancedIndirectStrategy — GPU 实例化，高性能，支持视锥体剔除
 *   2. MeshPointsStrategy        — Mesh 点拓扑，兼容回退
 * ==========================================
 */

using System;
using UnityEngine;

namespace OhMyPackage
{
    /// <summary>点云渲染策略枚举，用于运行时手动切换渲染路径</summary>
    public enum PointCloudRenderStrategyType
    {
        /// <summary>自动选择（按优先级：InstancedIndirect → MeshPoints）</summary>
        Auto,
        /// <summary>GPU 实例化路径，高性能，支持视锥体剔除</summary>
        InstancedIndirect,
        /// <summary>Mesh 点拓扑路径，兼容回退</summary>
        MeshPoints,
    }

    public class PointCloudRenderer : MonoBehaviour
    {
        #region Inspector 参数

        [Header("渲染配置")]
        [Tooltip("是否启用 GPU 视锥体剔除（仅 InstancedIndirect 策略支持，需要 Compute Shader）")]
        [SerializeField] private bool _enableFrustumCulling = false;

        [Tooltip("点大小（InstancedIndirect：世界空间单位；MeshPoints：屏幕像素）")]
        [SerializeField, Range(0.001f, 1f)] private float _pointSize = 0.05f;

        [Tooltip("渲染 Layer（0 = 使用 GameObject 自身 Layer）")]
        [SerializeField] private int _renderingLayer = 0;

        [Tooltip("视锥体剔除用 ComputeShader（FrustumCulling.compute），启用剔除时必须赋值，无需放入 Resources 文件夹")]
        [SerializeField] private ComputeShader _frustumCullingCompute;

        [Header("渲染相机（留空则自动使用 Camera.main）")]
        [SerializeField] private Camera _renderCamera;

        #endregion

        #region 私有字段

        private IPointCloudRenderStrategy _strategy;
        private bool _isInitialized = false;
        private bool _isDirty       = false;

        // 点云数据（所有权在此，策略只读取，不持有引用）
        private Vector3[] _positions = Array.Empty<Vector3>();
        private Color32[] _colors    = Array.Empty<Color32>();
        private int       _pointCount = 0;
        private Bounds    _bounds    = new Bounds(Vector3.zero, Vector3.zero);  // SetPoints 后由 CalculateBounds 填充，初始为零防止默认大包围盒规避剔除

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            if (_renderCamera == null)
                _renderCamera = Camera.main;
        }

        private void OnDestroy()
        {
            Release();
        }

        private void Update()
        {
            if (!_isInitialized || _pointCount == 0) return;

            if (_isDirty)
            {
                _strategy.RebuildBuffers(_positions, _colors, _pointCount, _pointSize);
                _isDirty = false;
            }

            int layer = _renderingLayer > 0 ? _renderingLayer : gameObject.layer;
            _strategy.Draw(_bounds, _renderCamera, layer);
        }

        #endregion

        #region 公开接口

        /// <summary>
        /// 是否启用视锥体剔除。切换后立即同步到当前策略；
        /// 若策略不支持剔除（如 MeshPoints），修改为 no-op。
        /// </summary>
        public bool EnableFrustumCulling
        {
            get => _enableFrustumCulling;
            set
            {
                if (_enableFrustumCulling == value) return;
                _enableFrustumCulling = value;
                if (_strategy != null) _strategy.EnableFrustumCulling = value;
            }
        }

        /// <summary>
        /// 点大小。修改后标记脏位，下一帧重建 Buffer。
        /// InstancedIndirect 路径：控制 TRS 矩阵 scale（世界空间单位）。
        /// MeshPoints 路径：控制材质 _PointSize（屏幕像素）。
        /// </summary>
        public float PointSize
        {
            get => _pointSize;
            set { _pointSize = Mathf.Max(0.001f, value); _isDirty = true; }
        }

        /// <summary>当前激活的渲染策略名称（只读，用于调试）</summary>
        public string CurrentRenderPath => _strategy?.Name ?? "Uninitialized";

        /// <summary>当前点数量</summary>
        public int PointCount => _pointCount;

        /// <summary>
        /// 手动初始化渲染器（可选）。
        /// 首次调用 SetPoints 时若未初始化会自动触发。
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) Release();
            _strategy      = SelectStrategy(BuildConfig());
            _isInitialized = true;
            Debug.Log($"[PointCloudRenderer] 初始化完成 | Strategy={_strategy.Name} | Culling={_enableFrustumCulling}");
        }

        /// <summary>提交点云数据（仅位置，MeshPoints 路径颜色默认白色）</summary>
        public void SetPoints(Vector3[] positions) => SetPoints(positions, null);

        /// <summary>
        /// 提交点云数据（位置 + 逐点颜色）。每次调用会完整替换现有数据。
        /// 注意：InstancedIndirect 路径颜色由 Shader 按高度自动计算，colors 数组在此路径无效。
        /// </summary>
        public void SetPoints(Vector3[] positions, Color32[] colors)
        {
            if (!_isInitialized) Initialize();
            _positions  = positions ?? Array.Empty<Vector3>();
            _colors     = colors    ?? Array.Empty<Color32>();
            _pointCount = _positions.Length;
            if (_pointCount > 0) _bounds = CalculateBounds(_positions);
            _isDirty = true;
        }

        /// <summary>
        /// 追加点云数据（不清空已有点，适用于增量更新场景）。
        /// 若需频繁大批量追加，建议改用 SetPoints 整体替换以减少数组扩容开销。
        /// </summary>
        public void AppendPoints(Vector3[] positions, Color32[] colors = null)
        {
            if (!_isInitialized) Initialize();
            int addCount = positions?.Length ?? 0;
            if (addCount == 0) return;

            int oldCount = _pointCount;
            int newCount = oldCount + addCount;
            Array.Resize(ref _positions, newCount);
            Array.Resize(ref _colors,    newCount);
            Array.Copy(positions, 0, _positions, oldCount, addCount);

            if (colors != null && colors.Length >= addCount)
                Array.Copy(colors, 0, _colors, oldCount, addCount);
            else
                for (int i = oldCount; i < newCount; i++)
                    _colors[i] = Color.white;

            _pointCount = newCount;
            _bounds     = CalculateBounds(_positions);
            _isDirty    = true;
        }

        /// <summary>清空所有点云数据并标记重建</summary>
        public void Clear()
        {
            _positions  = Array.Empty<Vector3>();
            _colors     = Array.Empty<Color32>();
            _pointCount = 0;
            _isDirty    = true;
        }

        /// <summary>设置渲染/剔除相机（默认 Camera.main）</summary>
        public void SetCamera(Camera cam) => _renderCamera = cam != null ? cam : Camera.main;

        /// <summary>
        /// 手动指定包围盒（影响 DrawMeshInstancedIndirect 的摄像机可见性判断）。
        /// 不调用则由 SetPoints 根据点云位置自动计算 AABB。
        /// </summary>
        public void SetBounds(Bounds bounds) => _bounds = bounds;

        /// <summary>释放所有渲染资源（OnDestroy 时自动调用）</summary>
        public void Release()
        {
            _strategy?.Release();
            _strategy      = null;
            _isInitialized = false;
        }

        /// <summary>
        /// 运行时切换渲染策略，切换后保留现有点云数据，下一帧自动重建 Buffer。
        /// Auto 模式按优先级自动选择：InstancedIndirect → MeshPoints。
        /// </summary>
        public void SwitchStrategy(PointCloudRenderStrategyType type)
        {
            _strategy?.Release();
            _strategy = null;

            var config = BuildConfig();
            _strategy = type switch
            {
                PointCloudRenderStrategyType.InstancedIndirect => TryCreateStrategy(new InstancedIndirectStrategy(), config),
                PointCloudRenderStrategyType.MeshPoints        => TryCreateStrategy(new MeshPointsStrategy(), config),
                _                                               => SelectStrategy(config),
            };
            _isInitialized = true;
            _isDirty       = true;
            Debug.Log($"[PointCloudRenderer] 策略切换 → {_strategy.Name}");
        }

        #endregion

        #region 私有方法

        /// <summary>创建指定策略，若初始化失败则回退到 MeshPoints</summary>
        private static IPointCloudRenderStrategy TryCreateStrategy(IPointCloudRenderStrategy strategy, PointCloudRenderConfig config)
        {
            if (strategy.TryInitialize(config)) return strategy;
            Debug.LogWarning($"[PointCloudRenderer] {strategy.Name} 初始化失败，回退到 MeshPoints");
            var fallback = new MeshPointsStrategy();
            fallback.TryInitialize(config);
            return fallback;
        }

        private PointCloudRenderConfig BuildConfig() => new PointCloudRenderConfig
        {
            EnableFrustumCulling    = _enableFrustumCulling,
            PointSize               = _pointSize,
            Parent                  = transform,
            FrustumCullingCompute   = _frustumCullingCompute,
        };

        /// <summary>
        /// 按优先级尝试初始化候选策略，返回第一个初始化成功的策略。
        /// 新增渲染策略只需在此数组中插入，无需修改其他逻辑。
        /// </summary>
        private static IPointCloudRenderStrategy SelectStrategy(PointCloudRenderConfig config)
        {
            IPointCloudRenderStrategy[] candidates =
            {
                new InstancedIndirectStrategy(),
                new MeshPointsStrategy(),
            };

            foreach (var candidate in candidates)
            {
                if (candidate.TryInitialize(config))
                    return candidate;
            }

            Debug.LogError("[PointCloudRenderer] 所有渲染策略均初始化失败！");
            // 安全回退：MeshPoints 不依赖特殊硬件，TryInitialize 应始终返回 true
            var fallback = new MeshPointsStrategy();
            fallback.TryInitialize(config);
            return fallback;
        }

        /// <summary>
        /// 遍历点集计算 AABB，并向外扩展 1 单位，
        /// 防止边缘点因浮点误差被 DrawMeshInstancedIndirect 的可见性测试错误剔除。
        /// </summary>
        private static Bounds CalculateBounds(Vector3[] positions)
        {
            if (positions == null || positions.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Vector3 min = positions[0], max = positions[0];
            for (int i = 1; i < positions.Length; i++)
            {
                min = Vector3.Min(min, positions[i]);
                max = Vector3.Max(max, positions[i]);
            }

            var b = new Bounds();
            b.SetMinMax(min, max);
            b.Expand(1f);
            return b;
        }

        #endregion

        #region Editor Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // 半透明填充
            Gizmos.color = new Color(0f, 1f, 1f, 0.05f);
            Gizmos.DrawCube(_bounds.center, _bounds.size);
            // 实线框
            Gizmos.color = new Color(0f, 1f, 1f, 0.8f);
            Gizmos.DrawWireCube(_bounds.center, _bounds.size);
        }
#endif

        #endregion
    }
}
