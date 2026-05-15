using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 本地文件缓存管理器。
    /// <para>核心职责：
    /// <list type="bullet">
    ///   <item>维护一份 JSON 清单，记录"远程标识 → 本地文件路径 + MD5"的映射。</item>
    ///   <item>通过 MD5 对比判断本地文件是否与远程版本一致，避免重复下载。</item>
    ///   <item>支持容量限制与 LRU 淘汰策略。</item>
    /// </list>
    /// </para>
    /// <para>设计参考：Unity AssetBundle.Caching、HTTP ETag 机制、npm content-addressable cache。</para>
    ///
    /// <example>
    /// <code>
    /// // 1. 初始化（应用启动时调用一次）
    /// var config = new FileCacheConfig.Builder()
    ///     .SetCacheDirectory(Path.Combine(Application.persistentDataPath, "MyCache"))
    ///     .SetMaxCacheBytes(500 * 1024 * 1024) // 500 MB
    ///     .Build();
    /// var cacheManager = new FileCacheManager(config, new MD5FileHashProvider());
    ///
    /// // 2. 下载前检查
    /// string url = "https://cdn.example.com/model.glb";
    /// string remoteMd5 = "d41d8cd98f00b204e9800998ecf8427e";
    ///
    /// if (cacheManager.TryGetValid(url, remoteMd5, out string localPath)) {
    ///     // 命中缓存，直接使用 localPath
    /// } else {
    ///     // 需要下载 → 你的下载逻辑 ...
    ///     string downloadedPath = DownloadFile(url);
    ///     cacheManager.Record(url, downloadedPath);
    /// }
    ///
    /// // 3. 清理过期缓存
    /// cacheManager.Cleanup(TimeSpan.FromDays(30));
    /// </code>
    /// </example>
    /// </summary>
    public sealed class DiskCacheManager : IDiskCache
    {
        private readonly DiskCacheConfig _config;
        private readonly IFileHashProvider _hashProvider;
        private FileCacheManifest _manifest;
        private readonly Dictionary<string, int> _keyIndex; // key → _manifest.Entries 索引
        private readonly object _lock = new object();
        private bool _disposed;

        /// <summary>当前缓存条目数。</summary>
        public int Count
        {
            get {
                lock (_lock) {
                    return _manifest.Entries.Count;
                }
            }
        }

        /// <summary>
        /// 使用指定配置和哈希提供器创建管理器并加载已有清单。由 VContainer 通过构造函数注入。
        /// </summary>
        public DiskCacheManager(DiskCacheConfig config, IFileHashProvider hashProvider)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
            _keyIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            EnsureCacheDirectory();
            _manifest = LoadManifest();
            RebuildIndex();
        }

        #region 查询 API

        /// <summary>
        /// 检查指定 key 是否已缓存（不校验 MD5，仅检查条目和文件存在性）。
        /// </summary>
        /// <param name="key">文件唯一标识（URL 或资源 ID）。</param>
        /// <param name="localPath">命中时输出本地路径。</param>
        /// <returns>命中且本地文件存在时返回 true。</returns>
        public bool TryGetCached(string key, out string localPath)
        {
            if (string.IsNullOrEmpty(key)) {
                localPath = null;
                return false;
            }

            lock (_lock) {
                if (_keyIndex.TryGetValue(key, out int idx)) {
                    var entry = _manifest.EntriesMutable[idx];

                    if (_config.VerifyFileExistence && !File.Exists(entry.LocalPath)) {
                        RemoveEntryAt(idx);
                        SaveIfAuto();
                        localPath = null;
                        return false;
                    }

                    entry.TouchAccess();
                    localPath = entry.LocalPath;
                    return true;
                }
            }

            localPath = null;
            return false;
        }

        /// <summary>
        /// 检查指定 key 是否已缓存且 MD5 匹配。
        /// 当远程文件的 MD5 已知时，用此方法判断是否需要重新下载。
        /// </summary>
        /// <param name="key">文件唯一标识。</param>
        /// <param name="expectedMd5">远程文件的 MD5（大小写不敏感）。</param>
        /// <param name="localPath">命中时输出本地路径。</param>
        /// <returns>缓存存在、文件存在且 MD5 一致时返回 true。</returns>
        public bool TryGetValid(string key, string expectedMd5, out string localPath)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(expectedMd5)) {
                localPath = null;
                return false;
            }

            lock (_lock) {
                if (_keyIndex.TryGetValue(key, out int idx)) {
                    var entry = _manifest.EntriesMutable[idx];

                    if (!string.Equals(entry.Md5, expectedMd5, StringComparison.OrdinalIgnoreCase)) {
                        localPath = null;
                        return false;
                    }

                    if (_config.VerifyFileExistence && !File.Exists(entry.LocalPath)) {
                        RemoveEntryAt(idx);
                        SaveIfAuto();
                        localPath = null;
                        return false;
                    }

                    entry.TouchAccess();
                    localPath = entry.LocalPath;
                    return true;
                }
            }

            localPath = null;
            return false;
        }

        /// <summary>
        /// 检查指定哈希值的文件是否已缓存（不校验 key，仅检查条目和文件存在性）。
        /// </summary>
        /// <param name="hash">文件的 MD5 哈希值。</param>
        /// <param name="localPath">命中时输出本地路径。</param>
        /// <returns>缓存存在且文件存在时返回 true。</returns>
        public bool TryGetCachedByHash(string hash, out string localPath)
        {
            if (string.IsNullOrEmpty(hash)) {
                localPath = null;
                return false;
            }

            lock (_lock) {
                foreach (var entry in _manifest.EntriesMutable) {
                    if (string.Equals(entry.Md5, hash, StringComparison.OrdinalIgnoreCase)) {
                        if (_config.VerifyFileExistence && !File.Exists(entry.LocalPath)) {
                            RemoveEntryAt(_keyIndex[entry.Key]);
                            SaveIfAuto();
                            localPath = null;
                            return false;
                        }

                        entry.TouchAccess();
                        localPath = entry.LocalPath;
                        return true;
                    }
                }
            }

            localPath = null;
            return false;
        }

        /// <summary>
        /// 获取指定 key 的缓存条目（只读快照）。
        /// </summary>
        public bool TryGetEntry(string key, out FileCacheEntry entry)
        {
            if (string.IsNullOrEmpty(key)) {
                entry = null;
                return false;
            }

            lock (_lock) {
                if (_keyIndex.TryGetValue(key, out int idx)) {
                    entry = _manifest.EntriesMutable[idx];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>指定 key 是否存在于清单中。</summary>
        public bool Contains(string key)
        {
            if (string.IsNullOrEmpty(key)) {
                return false;
            }

            lock (_lock) {
                return _keyIndex.ContainsKey(key);
            }
        }

        #endregion

        #region 写入 API

        /// <summary>
        /// 记录一个已下载的文件到缓存清单（自动计算 MD5 和文件大小）。
        /// </summary>
        /// <param name="key">文件唯一标识。</param>
        /// <param name="localPath">本地文件的绝对路径。</param>
        public void Record(string key, string localPath)
        {
            if (string.IsNullOrEmpty(key)) {
                throw new ArgumentException("key must not be null or empty.", nameof(key));
            }
            if (string.IsNullOrEmpty(localPath)) {
                throw new ArgumentException("localPath must not be null or empty.", nameof(localPath));
            }
            if (!File.Exists(localPath)) {
                throw new FileNotFoundException("Cannot record a file that does not exist.", localPath);
            }

            string md5 = _hashProvider.ComputeHash(localPath);
            long size = new FileInfo(localPath).Length;

            RecordInternal(key, md5, localPath, size);
        }

        /// <summary>
        /// 异步记录一个已下载的文件到缓存清单。
        /// </summary>
        public async Task RecordAsync(string key, string localPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key)) {
                throw new ArgumentException("key must not be null or empty.", nameof(key));
            }
            if (string.IsNullOrEmpty(localPath)) {
                throw new ArgumentException("localPath must not be null or empty.", nameof(localPath));
            }
            if (!File.Exists(localPath)) {
                throw new FileNotFoundException("Cannot record a file that does not exist.", localPath);
            }

            string md5 = await _hashProvider.ComputeHashAsync(localPath, cancellationToken);
            long size = new FileInfo(localPath).Length;

            RecordInternal(key, md5, localPath, size);
        }

        /// <summary>
        /// 记录文件到缓存清单（手动提供 MD5，跳过计算）。
        /// 当 MD5 已由服务端返回时，使用此方法避免二次计算。
        /// </summary>
        /// <param name="key">文件唯一标识。</param>
        /// <param name="md5">文件 MD5 哈希。</param>
        /// <param name="localPath">本地文件路径。</param>
        /// <param name="size">文件大小（字节）。传 0 时自动获取。</param>
        public void Record(string key, string md5, string localPath, long size = 0)
        {
            if (string.IsNullOrEmpty(key)) {
                throw new ArgumentException("key must not be null or empty.", nameof(key));
            }
            if (string.IsNullOrEmpty(localPath)) {
                throw new ArgumentException("localPath must not be null or empty.", nameof(localPath));
            }

            if (size <= 0 && File.Exists(localPath)) {
                size = new FileInfo(localPath).Length;
            }

            RecordInternal(key, md5, localPath, size);
        }

        /// <summary>移除指定 key 的缓存记录（不删除本地文件）。</summary>
        /// <returns>是否成功移除。</returns>
        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) {
                return false;
            }

            lock (_lock) {
                if (_keyIndex.TryGetValue(key, out int idx)) {
                    RemoveEntryAt(idx);
                    SaveIfAuto();
                    return true;
                }
            }

            return false;
        }

        /// <summary>移除指定 key 的缓存记录并删除本地文件。</summary>
        /// <returns>是否成功移除记录。</returns>
        public bool RemoveWithFile(string key)
        {
            if (string.IsNullOrEmpty(key)) {
                return false;
            }

            lock (_lock) {
                if (_keyIndex.TryGetValue(key, out int idx)) {
                    var entry = _manifest.EntriesMutable[idx];
                    TryDeleteFile(entry.LocalPath);
                    RemoveEntryAt(idx);
                    SaveIfAuto();
                    return true;
                }
            }

            return false;
        }

        /// <summary>清空所有缓存记录（不删除本地文件）。</summary>
        public void ClearAll()
        {
            lock (_lock) {
                _manifest.EntriesMutable.Clear();
                _keyIndex.Clear();
                SaveIfAuto();
            }
        }

        /// <summary>清空所有缓存记录并删除本地文件。</summary>
        public void ClearAllWithFiles()
        {
            lock (_lock) {
                foreach (var entry in _manifest.EntriesMutable) {
                    TryDeleteFile(entry.LocalPath);
                }
                _manifest.EntriesMutable.Clear();
                _keyIndex.Clear();
                SaveIfAuto();
            }
        }

        #endregion

        #region 清理 / 淘汰

        /// <summary>
        /// 清理超过指定年龄（基于最后访问时间）的缓存条目并删除对应文件。
        /// </summary>
        /// <param name="maxAge">最大保留时间。</param>
        /// <returns>清理的条目数。</returns>
        public int Cleanup(TimeSpan maxAge)
        {
            if (maxAge <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(maxAge), "maxAge must be positive.");
            }

            DateTime cutoff = DateTime.UtcNow - maxAge;
            int removed = 0;

            lock (_lock) {
                var entries = _manifest.EntriesMutable;
                for (int i = entries.Count - 1; i >= 0; i--) {
                    if (entries[i].LastAccessTimeUtc < cutoff) {
                        TryDeleteFile(entries[i].LocalPath);
                        entries.RemoveAt(i);
                        removed++;
                    }
                }
                if (removed > 0) {
                    RebuildIndex();
                    SaveIfAuto();
                }
            }

            return removed;
        }

        /// <summary>
        /// 清理已失效（本地文件已不存在）的缓存条目。
        /// </summary>
        /// <returns>清理的条目数。</returns>
        public int CleanupOrphaned()
        {
            int removed = 0;

            lock (_lock) {
                var entries = _manifest.EntriesMutable;
                for (int i = entries.Count - 1; i >= 0; i--) {
                    if (!File.Exists(entries[i].LocalPath)) {
                        entries.RemoveAt(i);
                        removed++;
                    }
                }
                if (removed > 0) {
                    RebuildIndex();
                    SaveIfAuto();
                }
            }

            return removed;
        }

        /// <summary>
        /// 当缓存总量超过 <see cref="DiskCacheConfig.MaxCacheBytes"/> 时，
        /// 按 LRU（最近最少使用）策略淘汰最旧的条目，直到总量降至限制以下。
        /// </summary>
        /// <returns>淘汰的条目数。</returns>
        public int EvictLRU()
        {
            if (_config.MaxCacheBytes <= 0) {
                return 0;
            }

            lock (_lock) {
                long totalSize = GetTotalSizeLocked();
                if (totalSize <= _config.MaxCacheBytes) {
                    return 0;
                }

                // 按最后访问时间升序排序（最旧的在前）
                var entries = _manifest.EntriesMutable;
                entries.Sort((a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));

                int removed = 0;
                while (entries.Count > 0 && totalSize > _config.MaxCacheBytes) {
                    totalSize -= entries[0].Size;
                    TryDeleteFile(entries[0].LocalPath);
                    entries.RemoveAt(0);
                    removed++;
                }

                if (removed > 0) {
                    RebuildIndex();
                    SaveIfAuto();
                }

                return removed;
            }
        }

        /// <summary>获取所有缓存文件的总大小（字节）。</summary>
        public long GetTotalSize()
        {
            lock (_lock) {
                return GetTotalSizeLocked();
            }
        }

        #endregion

        #region 持久化

        /// <summary>手动保存清单到磁盘。</summary>
        public void Save()
        {
            lock (_lock) {
                SaveManifest();
            }
        }

        /// <summary>从磁盘重新加载清单（丢弃内存中未保存的变更）。</summary>
        public void Reload()
        {
            lock (_lock) {
                _manifest = LoadManifest();
                RebuildIndex();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) {
                return;
            }

            lock (_lock) {
                if (!_disposed) {
                    SaveManifest();
                    _disposed = true;
                }
            }
        }

        #endregion

        #region 内部实现

        private void RecordInternal(string key, string md5, string localPath, long size)
        {
            lock (_lock) {
                if (_keyIndex.TryGetValue(key, out int idx)) {
                    _manifest.EntriesMutable[idx].Update(md5, localPath, size);
                } else {
                    var entry = new FileCacheEntry(key, md5, localPath, size);
                    _manifest.EntriesMutable.Add(entry);
                    _keyIndex[key] = _manifest.EntriesMutable.Count - 1;
                }

                SaveIfAuto();
            }

            // 容量限制检查（在锁外执行淘汰，避免嵌套锁）
            if (_config.MaxCacheBytes > 0) {
                EvictLRU();
            }
        }

        private void RebuildIndex()
        {
            _keyIndex.Clear();
            var entries = _manifest.EntriesMutable;
            for (int i = 0; i < entries.Count; i++) {
                if (entries[i] != null && !string.IsNullOrEmpty(entries[i].Key)) {
                    _keyIndex[entries[i].Key] = i;
                }
            }
        }

        private void RemoveEntryAt(int index)
        {
            var entries = _manifest.EntriesMutable;
            string removedKey = entries[index].Key;
            _keyIndex.Remove(removedKey);

            // 用末尾元素填补被删除的位置，避免 O(n) 移动
            int lastIndex = entries.Count - 1;
            if (index != lastIndex) {
                entries[index] = entries[lastIndex];
                _keyIndex[entries[index].Key] = index;
            }
            entries.RemoveAt(lastIndex);
        }

        private FileCacheManifest LoadManifest()
        {
            string path = _config.ManifestPath;
            if (!File.Exists(path)) {
                return new FileCacheManifest();
            }

            try {
                string json = File.ReadAllText(path);
                return FileCacheManifest.FromJson(json);
            }
            catch (Exception e) {
                Debug.LogWarning($"[FileCacheManager] Failed to load manifest: {e.Message}. Starting fresh.");
                return new FileCacheManifest();
            }
        }

        private void SaveManifest()
        {
            try {
                EnsureCacheDirectory();
                string json = _manifest.ToJson(true);
                string path = _config.ManifestPath;

                // 原子写入：先写临时文件，再替换目标文件，防止写入中途崩溃导致清单损坏
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(path)) {
                    File.Delete(path);
                }
                File.Move(tempPath, path);
            }
            catch (Exception e) {
                Debug.LogError($"[FileCacheManager] Failed to save manifest: {e.Message}");
            }
        }

        private void SaveIfAuto()
        {
            if (_config.AutoSave) {
                SaveManifest();
            }
        }

        private void EnsureCacheDirectory()
        {
            if (!Directory.Exists(_config.CacheDirectory)) {
                Directory.CreateDirectory(_config.CacheDirectory);
            }
        }

        private long GetTotalSizeLocked()
        {
            long total = 0;
            foreach (var entry in _manifest.EntriesMutable) {
                total += entry.Size;
            }
            return total;
        }

        private static void TryDeleteFile(string path)
        {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch (Exception e) {
                Debug.LogWarning($"[FileCacheManager] Failed to delete cached file '{path}': {e.Message}");
            }
        }

        #endregion
    }
}
