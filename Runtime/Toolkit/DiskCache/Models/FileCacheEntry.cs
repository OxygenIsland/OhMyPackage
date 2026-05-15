using System;
using UnityEngine;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 单条文件缓存记录，可被 JsonUtility 序列化。
    /// </summary>
    [Serializable]
    public sealed class FileCacheEntry
    {
        /// <summary>文件唯一标识（通常为下载 URL 或资源 ID）。</summary>
        [SerializeField] private string _key;

        /// <summary>文件内容的 MD5 哈希（小写十六进制，32 字符）。</summary>
        [SerializeField] private string _md5;

        /// <summary>本地缓存文件的绝对路径。</summary>
        [SerializeField] private string _localPath;

        /// <summary>文件大小（字节）。</summary>
        [SerializeField] private long _size;

        /// <summary>首次缓存时间（UTC，ISO 8601）。</summary>
        [SerializeField] private string _cachedTimeUtc;

        /// <summary>最近一次访问时间（UTC，ISO 8601）。</summary>
        [SerializeField] private string _lastAccessTimeUtc;

        public string Key => _key;
        public string Md5 => _md5;
        public string LocalPath => _localPath;
        public long Size => _size;

        public DateTime CachedTimeUtc =>
            DateTime.TryParse(_cachedTimeUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : DateTime.MinValue;

        public DateTime LastAccessTimeUtc =>
            DateTime.TryParse(_lastAccessTimeUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : DateTime.MinValue;

        /// <summary>
        /// 创建一条新的缓存记录。
        /// </summary>
        public FileCacheEntry(string key, string md5, string localPath, long size)
        {
            if (string.IsNullOrEmpty(key)) {
                throw new ArgumentException("key must not be null or empty.", nameof(key));
            }
            if (string.IsNullOrEmpty(localPath)) {
                throw new ArgumentException("localPath must not be null or empty.", nameof(localPath));
            }

            _key = key;
            _md5 = md5 ?? string.Empty;
            _localPath = localPath;
            _size = size;

            string now = DateTime.UtcNow.ToString("o");
            _cachedTimeUtc = now;
            _lastAccessTimeUtc = now;
        }

        /// <summary>
        /// 更新 MD5、路径及大小信息（文件被重新下载时调用）。
        /// </summary>
        public void Update(string md5, string localPath, long size)
        {
            if (string.IsNullOrEmpty(localPath)) {
                throw new ArgumentException("localPath must not be null or empty.", nameof(localPath));
            }

            _md5 = md5 ?? string.Empty;
            _localPath = localPath;
            _size = size;
            _lastAccessTimeUtc = DateTime.UtcNow.ToString("o");
        }

        /// <summary>刷新最近访问时间。</summary>
        public void TouchAccess()
        {
            _lastAccessTimeUtc = DateTime.UtcNow.ToString("o");
        }
    }
}
