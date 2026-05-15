using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyGame.Toolkit.DiskCache
{
    /// <summary>
    /// 缓存清单文件的序列化模型，由 <see cref="DiskCacheManager"/> 读写。
    /// <para>使用 <see cref="JsonUtility"/> 进行 JSON 序列化/反序列化，
    /// 保证零外部依赖、最大兼容性。</para>
    /// </summary>
    [Serializable]
    public sealed class FileCacheManifest
    {
        /// <summary>清单格式版本号，用于未来迁移。</summary>
        [SerializeField] private int _version = 1;

        /// <summary>所有缓存条目。</summary>
        [SerializeField] private List<FileCacheEntry> _entries = new List<FileCacheEntry>();

        public int Version => _version;

        /// <summary>缓存条目的只读视图。</summary>
        public IReadOnlyList<FileCacheEntry> Entries => _entries;

        /// <summary>内部写入用列表（仅 Manager 使用）。</summary>
        internal List<FileCacheEntry> EntriesMutable => _entries;

        /// <summary>
        /// 将清单序列化为 JSON 字符串。
        /// </summary>
        public string ToJson(bool prettyPrint = false)
        {
            return JsonUtility.ToJson(this, prettyPrint);
        }

        /// <summary>
        /// 从 JSON 字符串反序列化清单。
        /// </summary>
        /// <returns>成功返回清单实例；JSON 为空或格式异常返回全新空清单。</returns>
        public static FileCacheManifest FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) {
                return new FileCacheManifest();
            }

            try {
                var manifest = JsonUtility.FromJson<FileCacheManifest>(json);
                if (manifest == null) {
                    return new FileCacheManifest();
                }
                // 防御反序列化后 list 为 null 的情况
                if (manifest._entries == null) {
                    manifest._entries = new List<FileCacheEntry>();
                }
                return manifest;
            }
            catch (Exception e) {
                Debug.LogWarning($"[FileCacheManifest] Failed to parse manifest JSON, creating new one. Error: {e.Message}");
                return new FileCacheManifest();
            }
        }
    }
}
