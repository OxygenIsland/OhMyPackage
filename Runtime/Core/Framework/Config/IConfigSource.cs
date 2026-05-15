using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 配置数据源策略接口。
    /// <para>实现此接口以支持不同的配置加载来源（Resources、StreamingAssets、远端 URL 等）。
    /// 每个数据源负责读取并返回 JSON 格式的配置字符串，
    /// <see cref="IConfigManager"/> 负责后续的解析与合并。</para>
    ///
    /// <example>
    /// <code>
    /// // 从 Resources 文件夹加载
    /// var source = new ResourcesConfigSource("Configs/GlobalConfig");
    /// await configManager.LoadAsync(source);
    ///
    /// // 从 StreamingAssets 加载（支持热更替换）
    /// var source = new StreamingAssetsConfigSource("global_config.json");
    /// await configManager.LoadAsync(source);
    /// </code>
    /// </example>
    /// </summary>
    public interface IConfigSource
    {
        /// <summary>
        /// 数据源唯一标识符，用于日志记录和调试。
        /// </summary>
        string SourceId { get; }

        /// <summary>
        /// 异步读取配置内容，返回 JSON 格式字符串。
        /// <para>加载失败时返回 <c>null</c> 或空字符串，
        /// 具体错误信息应由实现内部记录日志。</para>
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>JSON 字符串，失败时为 <c>null</c>。</returns>
        UniTask<string> ReadAsync(CancellationToken cancellationToken = default);
    }
}
