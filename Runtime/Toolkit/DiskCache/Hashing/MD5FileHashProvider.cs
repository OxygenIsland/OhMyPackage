using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 基于 MD5 的 <see cref="IFileHashProvider"/> 默认实现。
    /// 内部委托给 <see cref="FileHashHelper"/> 静态工具类。
    /// </summary>
    public sealed class MD5FileHashProvider : IFileHashProvider
    {
        /// <summary>全局共享的单例实例。</summary>
        public static readonly MD5FileHashProvider Instance = new MD5FileHashProvider();

        public string ComputeHash(string filePath)
        {
            return FileHashHelper.ComputeMD5(filePath);
        }

        public Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return FileHashHelper.ComputeMD5Async(filePath, cancellationToken);
        }

        public bool Verify(string filePath, string expectedHash)
        {
            return FileHashHelper.Verify(filePath, expectedHash);
        }
    }
}
