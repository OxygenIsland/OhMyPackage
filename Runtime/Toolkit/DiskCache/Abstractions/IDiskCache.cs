using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 文件缓存管理器的抽象接口。
    /// <para>业务层应依赖此接口而非具体实现，便于：
    /// <list type="bullet">
    ///   <item>单元测试时注入 Mock，不触碰磁盘。</item>
    ///   <item>未来替换底层存储（如 SQLite）而不影响上层。</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IDiskCache : IDisposable
    {
        /// <summary>当前缓存条目数。</summary>
        int Count { get; }

        #region 查询

        /// <summary>
        /// 检查指定 key 是否已缓存（不校验哈希，仅检查条目和文件存在性）。
        /// </summary>
        bool TryGetCached(string key, out string localPath);

        /// <summary>
        /// 检查指定 key 是否已缓存且哈希匹配。
        /// </summary>
        bool TryGetValid(string key, string expectedHash, out string localPath);

        /// <summary>
        /// 检查指定哈希值的文件是否已缓存（不校验 key，仅检查条目和文件存在性）。
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="localPath"></param>
        /// <returns></returns>
        bool TryGetCachedByHash(string hash, out string localPath);

        /// <summary>获取指定 key 的缓存条目。</summary>
        bool TryGetEntry(string key, out FileCacheEntry entry);

        /// <summary>指定 key 是否存在于清单中。</summary>
        bool Contains(string key);

        #endregion

        #region 写入

        /// <summary>
        /// 记录一个已下载的文件到缓存清单（自动计算哈希和文件大小）。
        /// </summary>
        void Record(string key, string localPath);

        /// <summary>
        /// 异步记录一个已下载的文件到缓存清单。
        /// </summary>
        Task RecordAsync(string key, string localPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 记录文件到缓存清单（手动提供哈希，跳过计算）。
        /// </summary>
        void Record(string key, string hash, string localPath, long size = 0);

        /// <summary>移除指定 key 的缓存记录（不删除本地文件）。</summary>
        bool Remove(string key);

        /// <summary>移除指定 key 的缓存记录并删除本地文件。</summary>
        bool RemoveWithFile(string key);

        /// <summary>清空所有缓存记录（不删除本地文件）。</summary>
        void ClearAll();

        /// <summary>清空所有缓存记录并删除本地文件。</summary>
        void ClearAllWithFiles();

        #endregion

        #region 清理 / 淘汰

        /// <summary>清理超过指定年龄的缓存条目并删除对应文件。</summary>
        int Cleanup(TimeSpan maxAge);

        /// <summary>清理已失效（本地文件已不存在）的缓存条目。</summary>
        int CleanupOrphaned();

        /// <summary>按 LRU 策略淘汰条目直到总量降至限制以下。</summary>
        int EvictLRU();

        /// <summary>获取所有缓存文件的总大小（字节）。</summary>
        long GetTotalSize();

        #endregion

        #region 持久化

        /// <summary>手动保存清单到磁盘。</summary>
        void Save();

        /// <summary>从磁盘重新加载清单。</summary>
        void Reload();

        #endregion
    }
}
