using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NLog;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 从 Unity <c>Resources</c> 文件夹加载 JSON 配置的数据源。
    /// <para>
    /// 路径规则与 <see cref="Resources.Load"/> 相同：相对于任意 <c>Resources</c> 目录，
    /// 无需文件扩展名，例如 <c>"Configs/GlobalConfig"</c>。
    /// </para>
    /// <para>加载后会立即卸载 <see cref="TextAsset"/> 资源，不持有引用。</para>
    ///
    /// <example>
    /// <code>
    /// // Assets/Resources/Configs/GlobalConfig.json
    /// var source = new ResourcesConfigSource("Configs/GlobalConfig");
    /// bool ok = await configManager.LoadAsync(source);
    /// </code>
    /// </example>
    /// </summary>
    public sealed class ResourcesConfigSource : IConfigSource
    {
        private static readonly NLog.Logger Log = LogManager.GetCurrentClassLogger();

        private readonly string _resourcePath;

        /// <param name="resourcePath">
        /// Resources 相对路径（不含扩展名），例如 <c>"Configs/GlobalConfig"</c>。
        /// </param>
        public ResourcesConfigSource(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                throw new ArgumentNullException(nameof(resourcePath));
            _resourcePath = resourcePath;
        }

        /// <inheritdoc />
        public string SourceId => $"Resources:{_resourcePath}";

        /// <inheritdoc />
        public async UniTask<string> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Resources.LoadAsync 必须在主线程调用
            var request = Resources.LoadAsync<TextAsset>(_resourcePath);
            await request.ToUniTask(cancellationToken: cancellationToken);

            var asset = request.asset as TextAsset;
            if (asset == null)
            {
                Log.Warn("[ResourcesConfigSource] 未找到资源：{0}", _resourcePath);
                return null;
            }

            var text = asset.text;
            // 立即卸载，避免常驻内存
            Resources.UnloadAsset(asset);
            return text;
        }
    }
}
