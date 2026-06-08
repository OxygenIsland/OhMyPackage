using UnityEngine;

namespace OhMyPackage
{
    /// <summary>
    /// GPU 实例化渲染策略（DrawMeshInstancedIndirect）。
    ///
    /// 职责：
    ///   - 管理 positionInputBuffer / positionOutputBuffer / argsBuffer 的完整生命周期
    ///   - 可选：调用 FrustumCulling.compute（ViewPortCulling kernel）进行 GPU 视锥体剔除
    ///   - 每帧提交 DrawMeshInstancedIndirect 绘制命令
    ///
    /// 颜色说明：
    ///   颜色由 InstancedShader 根据世界空间 Y 轴高度自动渐变（蓝→青→绿→黄→红），
    ///   CPU 传入的 Color32 数组在此策略中不被使用。
    ///
    /// Buffer 布局：
    ///   每个点编码为 float4x4 TRS 矩阵（stride = 64 字节）。
    ///   Shader 读取：data._11 = uniform scale，data._14_24_34 = world position。
    /// </summary>
    public class InstancedIndirectStrategy : IPointCloudRenderStrategy
    {
        #region 内部常量

        private const string ShaderPath   = "Instanced/URP/InstancedShader";
        private const string ComputePath  = "FrustumCulling";        // Resources 路径，不含扩展名
        private const string ComputeKernel = "ViewPortCulling";       // numthreads(640,1,1)

        #endregion

        #region 私有字段

        private bool   _enableFrustumCulling;
        private int    _cachedPointCount;

        // GPU 资源
        private Mesh                  _instanceMesh;
        private Material              _instanceMaterial;
        private MaterialPropertyBlock _propertyBlock;
        private ComputeBuffer         _argsBuffer;
        private ComputeBuffer         _positionInputBuffer;
        private ComputeBuffer         _positionOutputBuffer;  // AppendStructuredBuffer（剔除输出）
        private uint[]                _args = new uint[5];    // [indexCount, instanceCount, indexStart, baseVertex, 0]

        // Compute Shader 视锥体剔除（FrustumCulling.compute，numthreads = 640,1,1）
        private ComputeShader _cullingCompute;
        private int           _cullingKernel;

        // Shader property ID 缓存，避免每帧字符串查找
        private static readonly int PropPositionBuffer = Shader.PropertyToID("positionBuffer");
        private static readonly int PropRenderingLayer = Shader.PropertyToID("_RenderingLayer");
        private static readonly int PropHeightMin      = Shader.PropertyToID("_HeightMin");
        private static readonly int PropHeightMax      = Shader.PropertyToID("_HeightMax");

        #endregion

        #region IPointCloudRenderStrategy

        public string Name => "InstancedIndirect";

        public bool EnableFrustumCulling
        {
            get => _enableFrustumCulling;
            set => _enableFrustumCulling = value;
        }

        public bool TryInitialize(PointCloudRenderConfig config)
        {
            Shader instancedShader = Shader.Find(ShaderPath);
            if (!SystemInfo.supportsInstancing || SystemInfo.graphicsShaderLevel < 45 || instancedShader == null)
            {
                if (instancedShader == null)
                    Debug.LogWarning($"[InstancedIndirectStrategy] Shader '{ShaderPath}' 未找到，降级到 MeshPoints 策略。");
                return false;
            }

            _enableFrustumCulling = config.EnableFrustumCulling;

            if (_enableFrustumCulling && !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[InstancedIndirectStrategy] 当前平台不支持 Compute Shader，自动禁用视锥体剔除。");
                _enableFrustumCulling = false;
            }

            // 从临时 Cube 取 SharedMesh（SharedMesh 由引擎管理，销毁 GameObject 不影响 Mesh 引用）
            var tempGo    = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _instanceMesh = tempGo.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tempGo);

            _instanceMaterial = new Material(instancedShader);
            _propertyBlock    = new MaterialPropertyBlock();
            _argsBuffer       = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

            if (_enableFrustumCulling)
            {
                if (config.FrustumCullingCompute == null)
                {
                    Debug.LogWarning("[InstancedIndirectStrategy] FrustumCullingCompute 未赋值，禁用视锥体剔除。");
                    _enableFrustumCulling = false;
                }
                else
                {
                    _cullingCompute = config.FrustumCullingCompute;
                    _cullingKernel  = _cullingCompute.FindKernel(ComputeKernel);
                }
            }

            return true;
        }

        public void RebuildBuffers(Vector3[] positions, Color32[] colors, int pointCount, float pointSize)
        {
            // 先释放旧 Buffer，再分配新 Buffer，避免 GPU 内存泄漏
            ReleaseBuffers();
            _cachedPointCount = pointCount;

            if (pointCount == 0) return;

            // 构建 TRS 矩阵数组：scale = pointSize（均匀缩放），rotation = identity
            var matrices = new Matrix4x4[pointCount];
            var scale    = Vector3.one * pointSize;
            for (int i = 0; i < pointCount; i++)
                matrices[i] = Matrix4x4.TRS(positions[i], Quaternion.identity, scale);

            // 输入 Buffer（StructuredBuffer<float4x4>，stride = 16 floats = 64 字节）
            _positionInputBuffer = new ComputeBuffer(pointCount, 16 * sizeof(float));
            _positionInputBuffer.SetData(matrices);

            // 剔除输出 Buffer（AppendStructuredBuffer，仅启用剔除时创建）
            if (_enableFrustumCulling)
                _positionOutputBuffer = new ComputeBuffer(pointCount, 16 * sizeof(float), ComputeBufferType.Append);

            // Args Buffer：[indexCount, instanceCount, indexStart, baseVertex, startInstance]
            _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            _args[0]    = _instanceMesh != null ? (uint)_instanceMesh.GetIndexCount(0)  : 0;
            _args[1]    = (uint)pointCount;
            _args[2]    = _instanceMesh != null ? (uint)_instanceMesh.GetIndexStart(0)  : 0;
            _args[3]    = _instanceMesh != null ? (uint)_instanceMesh.GetBaseVertex(0)  : 0;
            _args[4]    = 0;
            _argsBuffer.SetData(_args);
        }

        public void Draw(Bounds bounds, Camera camera, int layer)
        {
            if (_positionInputBuffer == null || _argsBuffer == null) return;

            // URP 下 DrawMeshInstancedIndirect 的 bounds 参数不保证执行视锥体剔除，
            // 在此手动做 CPU 端整批次裁剪，避免相机不可见时仍提交 DrawCall。
            if (camera != null)
            {
                Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                    return;
            }

            if (_enableFrustumCulling && _cullingCompute != null && _positionOutputBuffer != null && camera != null)
                DrawWithCulling(camera);
            else
                DrawDirect();

            if (layer > 0)
                _propertyBlock.SetInt(PropRenderingLayer, 1 << layer);

            Graphics.DrawMeshInstancedIndirect(
                _instanceMesh, 0, _instanceMaterial, bounds,
                _argsBuffer, 0, _propertyBlock,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                false, layer > 0 ? layer : 0);
        }

        public void Release()
        {
            ReleaseBuffers();
            if (_instanceMaterial != null) { Object.Destroy(_instanceMaterial); _instanceMaterial = null; }
            _propertyBlock = null;
        }

        #endregion

        #region 高度着色范围（可选，仅当 InstancedShader 支持对应属性时生效）

        /// <summary>
        /// 设置高度渐变范围。若 InstancedShader 中已将 minHeight/maxHeight 暴露为 Material Property，
        /// 可通过此方法在运行时调整；否则此调用为 no-op。
        /// </summary>
        public void SetHeightColorRange(float min, float max)
        {
            if (_instanceMaterial == null) return;
            if (_instanceMaterial.HasProperty(PropHeightMin))
                _instanceMaterial.SetFloat(PropHeightMin, min);
            if (_instanceMaterial.HasProperty(PropHeightMax))
                _instanceMaterial.SetFloat(PropHeightMax, max);
        }

        #endregion

        #region 私有方法

        private void DrawWithCulling(Camera camera)
        {
            // 1. 重置 AppendBuffer 计数器
            _positionOutputBuffer.SetCounterValue(0);

            // 2. Dispatch FrustumCulling.compute
            //    numthreads(640,1,1)，Dispatch 组数 = ceil(instanceCount / 640)
            Vector4[] planes = ExtractFrustumPlanes(camera);
            _cullingCompute.SetBuffer(_cullingKernel, "input",      _positionInputBuffer);
            _cullingCompute.SetBuffer(_cullingKernel, "cullresult", _positionOutputBuffer);
            _cullingCompute.SetInt("instanceCount", _cachedPointCount);
            _cullingCompute.SetVectorArray("planes", planes);
            int groups = Mathf.Max(1, Mathf.CeilToInt(_cachedPointCount / 640f));
            _cullingCompute.Dispatch(_cullingKernel, groups, 1, 1);

            // 3. 将 AppendBuffer 的实际元素数写入 argsBuffer[1]（字节偏移 = 1 * sizeof(uint)）
            _instanceMaterial.SetBuffer(PropPositionBuffer, _positionOutputBuffer);
            ComputeBuffer.CopyCount(_positionOutputBuffer, _argsBuffer, sizeof(uint));
        }

        private void DrawDirect()
        {
            // 若上一帧走了剔除路径（CopyCount 修改了 argsBuffer[1]），需恢复为全量点数
            _args[1] = (uint)_cachedPointCount;
            _argsBuffer.SetData(_args);
            _instanceMaterial.SetBuffer(PropPositionBuffer, _positionInputBuffer);
        }

        private void ReleaseBuffers()
        {
            _positionInputBuffer?.Release();  _positionInputBuffer  = null;
            _positionOutputBuffer?.Release(); _positionOutputBuffer = null;
            _argsBuffer?.Release();           _argsBuffer           = null;
        }

        /// <summary>
        /// 从相机提取视锥体 6 个裁剪平面，转换为 float4（法线 xyz + 距离 w），
        /// 与 FrustumCulling.compute 中 dot(plane.xyz, pos) + plane.w 的判断格式对应。
        /// </summary>
        private static Vector4[] ExtractFrustumPlanes(Camera cam)
        {
            Plane[]   planes = GeometryUtility.CalculateFrustumPlanes(cam);
            Vector4[] result = new Vector4[6];
            for (int i = 0; i < 6; i++)
                result[i] = new Vector4(
                    planes[i].normal.x, planes[i].normal.y,
                    planes[i].normal.z, planes[i].distance);
            return result;
        }

        #endregion
    }
}
