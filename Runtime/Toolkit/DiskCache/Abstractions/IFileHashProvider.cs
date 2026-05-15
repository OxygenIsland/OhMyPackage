using System.Threading;
using System.Threading.Tasks;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 文件哈希计算策略接口。
    /// <para>默认实现为 <see cref="MD5FileHashProvider"/>（MD5）。
    /// 如需使用 SHA256、CRC32 等算法，实现此接口并注入 <see cref="DiskCacheConfig"/> 即可。</para>
    /// </summary>
    public interface IFileHashProvider
    {
        /// <summary>
        /// 同步计算文件哈希。
        /// </summary>
        /// <param name="filePath">文件绝对路径。</param>
        /// <returns>小写十六进制字符串。</returns>
        string ComputeHash(string filePath);

        /// <summary>
        /// 异步计算文件哈希（适合大文件）。
        /// </summary>
        /// <param name="filePath">文件绝对路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>小写十六进制字符串。</returns>
        Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 验证文件哈希是否与期望值一致。
        /// </summary>
        /// <param name="filePath">文件路径。</param>
        /// <param name="expectedHash">期望的哈希值（大小写不敏感）。</param>
        bool Verify(string filePath, string expectedHash);
    }
}
