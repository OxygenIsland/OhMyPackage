/**
 * ==========================================
 * Author：albert
 * CreatTime：20250618
 * Description：PointCloudRenderer 使用示例
 *
 * 挂载即用：将此脚本挂载到任意 GameObject，运行后自动演示各功能。
 * ==========================================
 */

using System.Collections;
using UnityEngine;

namespace OhMyPackage.Sample
{

    /// <summary>
    /// PointCloudRenderer 使用示例，演示以下功能：
    ///   1. SetPoints    — 整体替换点云数据
    ///   2. AppendPoints — 增量追加点云数据
    ///   3. Clear        — 清空点云
    ///   4. PointSize    — 运行时修改点大小
    ///   5. SetBounds    — 手动指定包围盒
    ///   6. SetCamera    — 切换渲染相机
    /// </summary>
    [RequireComponent(typeof(PointCloudRenderer))]
    public class PointCloudRendererDemo : MonoBehaviour
    {
        #region Inspector 参数

        [Header("演示数据参数")]
        [Tooltip("球形点云的点数量")]
        [SerializeField] private int _pointCount = 5000;

        [Tooltip("球形点云半径")]
        [SerializeField] private float _radius = 3f;

        [Tooltip("演示循环间隔（秒）")]
        [SerializeField] private float _demoInterval = 3f;

        [Header("点大小范围（演示动态变化用）")]
        [SerializeField] private float _pointSizeMin = 0.02f;
        [SerializeField] private float _pointSizeMax = 0.15f;

        [Header("策略切换（运行时按 Tab 手动循环切换）")]
        [Tooltip("演示自动流程中切换到目标策略后的展示时长（秒）")]
        [SerializeField] private float _strategySwitchShowDuration = 3f;

        #endregion

        #region 私有字段

        private PointCloudRenderer _renderer;
        private int _demoStep = 0;

        // 策略切换循环顺序
        private static readonly PointCloudRenderStrategyType[] _strategyOrder =
        {
            PointCloudRenderStrategyType.Auto,
            PointCloudRenderStrategyType.InstancedIndirect,
            PointCloudRenderStrategyType.MeshPoints,
        };
        private int _strategyIndex = 0;

        #endregion

        #region Unity 生命周期

        private void Start()
        {
            _renderer = GetComponent<PointCloudRenderer>();
            Debug.Log($"[Demo] 当前渲染路径：{_renderer.CurrentRenderPath}");
            StartCoroutine(RunDemo());
        }

        private void Update()
        {
            // Tab 键手动循环切换渲染策略（不影响自动演示流程）
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _strategyIndex = (_strategyIndex + 1) % _strategyOrder.Length;
                var next = _strategyOrder[_strategyIndex];
                _renderer.SwitchStrategy(next);
                Debug.Log($"[Demo] 手动切换策略 → {next} | 当前路径：{_renderer.CurrentRenderPath}");
            }
        }

        #endregion

        #region 演示流程

        private IEnumerator RunDemo()
        {
            // ── Step 0：SetPoints，仅位置，颜色默认白色 ─────────────────
            _demoStep = 0;
            Debug.Log("[Demo] Step 0 — SetPoints（均匀球面分布，白色）");
            _renderer.SetPoints(GenerateSphere(_pointCount, _radius));
            yield return new WaitForSeconds(_demoInterval);

            // ── Step 1：SetPoints，位置 + 颜色（经纬度映射颜色）─────────
            _demoStep = 1;
            Debug.Log("[Demo] Step 1 — SetPoints（位置 + 逐点颜色）");
            Vector3[] positions = GenerateSphere(_pointCount, _radius);
            Color32[] colors    = GenerateSphereColors(positions);
            _renderer.SetPoints(positions, colors);
            yield return new WaitForSeconds(_demoInterval);

            // ── Step 2：AppendPoints，在内球附加更多点 ───────────────────
            _demoStep = 2;
            Debug.Log($"[Demo] Step 2 — AppendPoints（追加内球，当前总点数={_renderer.PointCount}）");
            Vector3[] inner = GenerateSphere(_pointCount / 2, _radius * 0.4f);
            Color32[] innerColors = GenerateUniformColor(inner.Length, new Color32(255, 200, 0, 255));
            _renderer.AppendPoints(inner, innerColors);
            Debug.Log($"[Demo] AppendPoints 后总点数={_renderer.PointCount}");
            yield return new WaitForSeconds(_demoInterval);

            // ── Step 3：动态修改点大小 ────────────────────────────────────
            _demoStep = 3;
            Debug.Log("[Demo] Step 3 — 动态修改 PointSize");
            float elapsed = 0f;
            while (elapsed < _demoInterval)
            {
                float t = Mathf.PingPong(elapsed, _demoInterval * 0.5f) / (_demoInterval * 0.5f);
                _renderer.PointSize = Mathf.Lerp(_pointSizeMin, _pointSizeMax, t);
                elapsed += Time.deltaTime;
                yield return null;
            }
            _renderer.PointSize = 0.05f;

            // ── Step 4：手动指定 Bounds ───────────────────────────────────
            _demoStep = 4;
            Debug.Log("[Demo] Step 4 — SetBounds（手动指定包围盒）");
            _renderer.SetBounds(new Bounds(transform.position, Vector3.one * (_radius * 2f + 2f)));
            yield return new WaitForSeconds(_demoInterval);

            // ── Step 6：Clear ─────────────────────────────────────────────
            _demoStep = 6;
            Debug.Log("[Demo] Step 6 — Clear（清空点云）");
            _renderer.Clear();
            yield return new WaitForSeconds(1f);

            // ── Step 7：回到 Step 0，循环演示 ────────────────────────────
            Debug.Log("[Demo] 演示完毕，重新开始...");
            StartCoroutine(RunDemo());
        }

        #endregion

        #region 数据生成工具

        /// <summary>用 Fibonacci 球面算法均匀生成球面点云</summary>
        private Vector3[] GenerateSphere(int count, float radius)
        {
            var pts     = new Vector3[count];
            float phi   = Mathf.PI * (3f - Mathf.Sqrt(5f));  // 黄金角
            for (int i = 0; i < count; i++)
            {
                float y     = 1f - (i / (float)(count - 1)) * 2f;
                float r     = Mathf.Sqrt(1f - y * y);
                float theta = phi * i;
                pts[i] = new Vector3(
                    Mathf.Cos(theta) * r,
                    y,
                    Mathf.Sin(theta) * r
                ) * radius + transform.position;
            }
            return pts;
        }

        /// <summary>根据点的球面坐标（经纬度）映射 HSV 颜色</summary>
        private Color32[] GenerateSphereColors(Vector3[] positions)
        {
            var colors = new Color32[positions.Length];
            Vector3 center = transform.position;
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 dir   = (positions[i] - center).normalized;
                float   hue   = (Mathf.Atan2(dir.z, dir.x) / (Mathf.PI * 2f) + 1f) % 1f;
                float   value = dir.y * 0.5f + 0.5f;
                colors[i] = Color32From(Color.HSVToRGB(hue, 0.9f, value));
            }
            return colors;
        }

        /// <summary>生成均匀单色数组</summary>
        private static Color32[] GenerateUniformColor(int count, Color32 color)
        {
            var arr = new Color32[count];
            for (int i = 0; i < count; i++) arr[i] = color;
            return arr;
        }

        private static Color32 Color32From(Color c) =>
            new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255);

        #endregion

        #region Gizmos（Editor 可视化包围盒）

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_renderer == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
#endif

        #endregion
    }
}
