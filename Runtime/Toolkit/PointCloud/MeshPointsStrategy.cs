using System.Collections.Generic;
using UnityEngine;

namespace OhMyPackage
{
    /// <summary>
    /// Mesh 点拓扑渲染策略（MeshTopology.Points + Geometry Shader）。
    ///
    /// 职责：
    ///   - 管理 PointCloud_MeshPoints 子 GameObject 及其 Mesh / Material 生命周期
    ///   - 将 positions 写入 Mesh 顶点，colors 写入顶点色（Color32 数组完全生效）
    ///   - Geometry Shader（VertexColorQuadConstSize）把每个顶点展开为屏幕固定像素大小的四边形
    ///
    /// 适用场景：
    ///   平台不支持 GPU 实例化（ShaderLevel < 4.5 或 Shader 缺失）时的兼容回退路径。
    ///   不支持视锥体剔除（EnableFrustumCulling 属性为 no-op）。
    /// </summary>
    public class MeshPointsStrategy : IPointCloudRenderStrategy
    {
        #region 内部常量

        private const string ShaderPath         = "PCDLib/VertexColor Quad ConstSize";
        private const string ShaderPathFallback = "Particles/Standard Unlit";

        #endregion

        #region 私有字段

        private GameObject   _rootObject;
        private Mesh         _mesh;
        private MeshFilter   _meshFilter;
        private MeshRenderer _meshRenderer;
        private Material     _material;
        private int[]        _indices;

        private static readonly int PropPointSize = Shader.PropertyToID("_PointSize");

        #endregion

        #region IPointCloudRenderStrategy

        public string Name => "MeshPoints";

        /// <summary>MeshPoints 策略不支持视锥体剔除，此属性为 no-op。</summary>
        public bool EnableFrustumCulling { get => false; set { } }

        public bool TryInitialize(PointCloudRenderConfig config)
        {
            _rootObject = new GameObject("PointCloud_MeshPoints");
            if (config.Parent != null)
                _rootObject.transform.SetParent(config.Parent, false);

            // MarkDynamic 告知 GPU 此 Mesh 会频繁更新，驱动可做相应优化
            _mesh             = new Mesh { name = "PointCloudMesh" };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.MarkDynamic();

            _meshFilter            = _rootObject.AddComponent<MeshFilter>();
            _meshFilter.sharedMesh = _mesh;

            _meshRenderer                   = _rootObject.AddComponent<MeshRenderer>();
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows    = false;

            Shader fallbackShader = Shader.Find(ShaderPath)
                                 ?? Shader.Find(ShaderPathFallback);

            _material = new Material(fallbackShader);
            if (_material.HasProperty(PropPointSize))
                _material.SetFloat(PropPointSize, config.PointSize);

            _meshRenderer.sharedMaterial = _material;
            return true;
        }

        public void RebuildBuffers(Vector3[] positions, Color32[] colors, int pointCount, float pointSize)
        {
            if (_mesh == null) return;

            _mesh.Clear();
            if (pointCount == 0) return;

            // 点大小换算：PointCloudRenderer._pointSize 语义为世界空间单位（同 InstancedIndirect）；
            // 此 Shader 的 _PointSize 为屏幕像素（Range 0.1–30）。
            // × 100 将典型值 0.05 m 换算为 ~5 px，0.15 m 换算为 ~15 px。
            if (_material != null && _material.HasProperty(PropPointSize))
            {
                float screenPx = Mathf.Clamp(pointSize * 100f, 0.1f, 30f);
                _material.SetFloat(PropPointSize, screenPx);
            }

            // 将世界空间坐标转换到 _rootObject 本地空间。
            // positions 由 PointCloudRenderer 提供，始终为世界坐标；
            // 若直接写入 Mesh，父节点 Transform 会被叠加两次导致渲染错位。
            var localPos = new Vector3[pointCount];
            Matrix4x4 w2l = _rootObject.transform.worldToLocalMatrix;
            for (int i = 0; i < pointCount; i++)
                localPos[i] = w2l.MultiplyPoint3x4(positions[i]);

            _mesh.SetVertices(localPos, 0, pointCount);

            // 颜色数组对齐：不足补白色，超出截断
            var colorList = new List<Color32>(pointCount);
            int srcCount  = Mathf.Min(colors.Length, pointCount);
            for (int i = 0; i < srcCount; i++)         colorList.Add(colors[i]);
            for (int i = srcCount; i < pointCount; i++) colorList.Add(Color.white);
            _mesh.SetColors(colorList);

            // 索引数组（0,1,2,...,N-1）仅在点数变化时重新分配，避免每帧 GC
            if (_indices == null || _indices.Length != pointCount)
            {
                _indices = new int[pointCount];
                for (int i = 0; i < pointCount; i++)
                    _indices[i] = i;
            }

            _mesh.SetIndices(_indices, MeshTopology.Points, 0, false);

            // 依据实际本地空间顶点自动计算包围盒，
            // 比在 Draw() 中手动传入世界坐标 Bounds 更准确。
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// MeshPoints 路径由 MeshRenderer 每帧自动提交。
        /// 包围盒已在 RebuildBuffers 中通过 RecalculateBounds 计算为本地空间，无需每帧重设。
        /// 此方法仅同步 GameObject.layer。
        /// </summary>
        public void Draw(Bounds bounds, Camera camera, int layer)
        {
            if (_rootObject != null && layer > 0)
                _rootObject.layer = layer;
        }

        public void Release()
        {
            if (_mesh        != null) { Object.Destroy(_mesh);        _mesh        = null; }
            if (_material    != null) { Object.Destroy(_material);    _material    = null; }
            if (_rootObject  != null) { Object.Destroy(_rootObject);  _rootObject  = null; }
            _meshFilter   = null;
            _meshRenderer = null;
            _indices      = null;
        }

        #endregion
    }
}
