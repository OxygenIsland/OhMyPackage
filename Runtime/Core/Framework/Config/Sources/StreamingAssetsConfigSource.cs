using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NLog;
using UnityEngine;
using UnityEngine.Networking;

namespace GameFramework
{
    /// <summary>
    /// 从 <c>StreamingAssets</c> 目录加载 JSON 配置的数据源。
    /// <para>
    /// 路径为相对于 <see cref="Application.streamingAssetsPath"/> 的子路径，例如 <c>"global_config.json"</c>。
    /// </para>
    /// <para>
    /// 在 Android 平台，StreamingAssets 内的文件被压缩在 APK 内部，必须通过 <see cref="UnityWebRequest"/>
    /// 访问；在 PC / macOS / iOS 上则直接使用 <see cref="File"/> IO 读取，以减少开销。
    /// 本实现对两条路径进行了统一封装，对调用方透明。
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // StreamingAssets/Configs/global_config.json
    /// var source = new StreamingAssetsConfigSource("Configs/global_config.json");
    /// bool ok = await configManager.LoadAsync(source);
    /// </code>
    /// </example>
    /// </summary>
    public sealed class StreamingAssetsConfigSource : IConfigSource
    {
        private static readonly NLog.Logger Log = LogManager.GetCurrentClassLogger();

        private readonly string _relativePath;

        /// <param name="relativePath">
        /// 相对于 <see cref="Application.streamingAssetsPath"/> 的文件路径，
        /// 例如 <c>"Configs/global_config.json"</c>。
        /// </param>
        public StreamingAssetsConfigSource(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentNullException(nameof(relativePath));
            _relativePath = relativePath;
        }

        /// <inheritdoc />
        public string SourceId => $"StreamingAssets:{_relativePath}";

        /// <inheritdoc />
        public async UniTask<string> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

#if UNITY_ANDROID && !UNITY_EDITOR
            return await ReadViaWebRequestAsync(cancellationToken);
#else
            return await ReadViaFileIOAsync(cancellationToken);
#endif
        }

        // ── 非 Android：直接文件 IO（线程池）───────────────────────────

        private async UniTask<string> ReadViaFileIOAsync(CancellationToken cancellationToken)
        {
            var fullPath = Path.Combine(Application.streamingAssetsPath, _relativePath);
            if (!File.Exists(fullPath))
            {
                Log.Warn("[StreamingAssetsConfigSource] 文件不存在：{0}", fullPath);
                return null;
            }

            try
            {
                var text = await UniTask.RunOnThreadPool(
                    () => File.ReadAllText(fullPath, Encoding.UTF8),
                    cancellationToken: cancellationToken);
                return text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[StreamingAssetsConfigSource] 读取文件失败：{0}", fullPath);
                return null;
            }
        }

        // ── Android：必须走 UnityWebRequest ──────────────────────────────

        private async UniTask<string> ReadViaWebRequestAsync(CancellationToken cancellationToken)
        {
            var uri = Path.Combine(Application.streamingAssetsPath, _relativePath)
                          .Replace("\\", "/");

            using var request = UnityWebRequest.Get(uri);
            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[StreamingAssetsConfigSource] UnityWebRequest 异常：{0}", uri);
                return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.Warn("[StreamingAssetsConfigSource] 请求失败 ({0})：{1}",
                    request.responseCode, request.error);
                return null;
            }

            return request.downloadHandler.text;
        }
    }
}
