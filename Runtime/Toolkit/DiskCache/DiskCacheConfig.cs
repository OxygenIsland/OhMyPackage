using System.IO;
using UnityEngine;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 文件缓存系统的配置。
    /// 使用 Builder 模式便于链式配置。
    /// </summary>
    public sealed class DiskCacheConfig
    {
        /// <summary>缓存根目录（默认: Application.persistentDataPath/FileCache）。</summary>
        public string CacheDirectory { get; }

        /// <summary>清单文件名（默认: cache_manifest.json）。</summary>
        public string ManifestFileName { get; }

        /// <summary>缓存最大容量（字节）。0 表示不限制。</summary>
        public long MaxCacheBytes { get; }

        /// <summary>是否在每次写入后立即持久化清单（默认 true）。关闭后需手动调用 Save。</summary>
        public bool AutoSave { get; }

        /// <summary>在读取缓存时是否验证本地文件存在性（默认 true）。</summary>
        public bool VerifyFileExistence { get; }

        /// <summary>清单文件的完整路径。</summary>
        public string ManifestPath => Path.Combine(CacheDirectory, ManifestFileName);

        private DiskCacheConfig(Builder builder)
        {
            CacheDirectory = builder.CacheDirectory;
            ManifestFileName = builder.ManifestFileName;
            MaxCacheBytes = builder.MaxCacheBytes;
            AutoSave = builder.AutoSave;
            VerifyFileExistence = builder.VerifyFileExistence;
        }

        /// <summary>使用默认配置创建实例。</summary>
        public static DiskCacheConfig Default => new Builder().Build();

        /// <summary>
        /// 配置构造器。
        /// </summary>
        public sealed class Builder
        {
            internal string CacheDirectory;
            internal string ManifestFileName = "cache_manifest.json";
            internal long MaxCacheBytes;
            internal bool AutoSave = true;
            internal bool VerifyFileExistence = true;

            public Builder()
            {
                CacheDirectory = Path.Combine(Application.persistentDataPath, "FileCache");
            }

            /// <summary>设置缓存目录。</summary>
            public Builder SetCacheDirectory(string directory)
            {
                CacheDirectory = directory;
                return this;
            }

            /// <summary>设置清单文件名。</summary>
            public Builder SetManifestFileName(string fileName)
            {
                ManifestFileName = fileName;
                return this;
            }

            /// <summary>设置缓存最大容量（字节），0 表示不限制。</summary>
            public Builder SetMaxCacheBytes(long maxBytes)
            {
                MaxCacheBytes = maxBytes;
                return this;
            }

            /// <summary>设置是否自动保存清单。</summary>
            public Builder SetAutoSave(bool autoSave)
            {
                AutoSave = autoSave;
                return this;
            }

            /// <summary>设置是否在读取时验证文件存在性。</summary>
            public Builder SetVerifyFileExistence(bool verify)
            {
                VerifyFileExistence = verify;
                return this;
            }

            public DiskCacheConfig Build()
            {
                return new DiskCacheConfig(this);
            }
        }
    }
}
