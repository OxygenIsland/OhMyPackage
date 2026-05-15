using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace GameFramework
{
    /// <summary>
    /// 全局配置管理器。
    /// <para>
    /// 以 <see cref="JObject"/> 作为内部存储结构，支持：
    /// <list type="bullet">
    ///   <item>通过 <see cref="IConfigSource"/> 策略模式从不同来源异步加载 JSON 配置。</item>
    ///   <item>多源加载与合并（Last-Write-Wins 覆盖策略）。</item>
    ///   <item>扁平键值访问（兼容原 GameFramework Config 风格）。</item>
    ///   <item>强类型 Section 反序列化（推荐用于新业务）。</item>
    ///   <item>运行时动态写入与清除。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class ConfigManager : IConfigManager, IDisposable
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private readonly object _lock = new object();
        private JObject _root = new JObject();
        private bool _disposed;

        /// <inheritdoc />
        public int Count
        {
            get
            {
                lock (_lock)
                    return _root.Count;
            }
        }

        /// <inheritdoc />
        public event Action<string> OnLoadSuccess;

        /// <inheritdoc />
        public event Action<string, string> OnLoadFailure;

        // ── 加载 ─────────────────────────────────────────────────────────

        /// <inheritdoc />
        public async UniTask<bool> LoadAsync(IConfigSource source, CancellationToken cancellationToken = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ThrowIfDisposed();

            string json;
            try
            {
                json = await source.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var errMsg = $"读取配置源时发生异常：{ex.Message}";
                Log.Error(ex, "[ConfigManager] 加载失败 sourceId={0}", source.SourceId);
                OnLoadFailure?.Invoke(source.SourceId, errMsg);
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                var errMsg = "配置源返回了空内容。";
                Log.Warn("[ConfigManager] 加载失败 sourceId={0}：{1}", source.SourceId, errMsg);
                OnLoadFailure?.Invoke(source.SourceId, errMsg);
                return false;
            }

            JObject parsed;
            try
            {
                parsed = JObject.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                var errMsg = $"JSON 解析失败：{ex.Message}";
                Log.Error(ex, "[ConfigManager] 加载失败 sourceId={0}", source.SourceId);
                OnLoadFailure?.Invoke(source.SourceId, errMsg);
                return false;
            }

            lock (_lock)
            {
                // 浅合并：顶层 Key 冲突时，后加载的值覆盖先前的值（Last-Write-Wins）
                _root.Merge(parsed, new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Replace,
                    MergeNullValueHandling = MergeNullValueHandling.Merge
                });
            }

            Log.Info("[ConfigManager] 加载成功 sourceId={0}，条目数={1}", source.SourceId, Count);
            OnLoadSuccess?.Invoke(source.SourceId);
            return true;
        }

        // ── 键值查询 ─────────────────────────────────────────────────────

        /// <inheritdoc />
        public bool HasConfig(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
                return _root.ContainsKey(key);
        }

        /// <inheritdoc />
        public T GetSection<T>() where T : class, new()
        {
            lock (_lock)
            {
                try
                {
                    return _root.ToObject<T>() ?? new T();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[ConfigManager] GetSection<{0}> 失败", typeof(T).Name);
                    return new T();
                }
            }
        }

        /// <inheritdoc />
        public T GetSection<T>(string sectionKey) where T : class, new()
        {
            if (string.IsNullOrEmpty(sectionKey)) return new T();
            lock (_lock)
            {
                try
                {
                    var token = _root[sectionKey];
                    if (token == null) return new T();
                    return token.ToObject<T>() ?? new T();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[ConfigManager] GetSection<{0}>(key={1}) 失败", typeof(T).Name, sectionKey);
                    return new T();
                }
            }
        }

        /// <inheritdoc />
        public bool GetBool(string key, bool defaultValue = false)
            => TryGetValue(key, out var token) ? token.Value<bool>() : defaultValue;

        /// <inheritdoc />
        public int GetInt(string key, int defaultValue = 0)
            => TryGetValue(key, out var token) ? token.Value<int>() : defaultValue;

        /// <inheritdoc />
        public float GetFloat(string key, float defaultValue = 0f)
            => TryGetValue(key, out var token) ? token.Value<float>() : defaultValue;

        /// <inheritdoc />
        public string GetString(string key, string defaultValue = "")
            => TryGetValue(key, out var token) ? token.Value<string>() ?? defaultValue : defaultValue;

        // ── 运行时修改 ───────────────────────────────────────────────────

        /// <inheritdoc />
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            lock (_lock)
                _root[key] = value;
        }

        /// <inheritdoc />
        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (_lock)
                return _root.Remove(key);
        }

        /// <inheritdoc />
        public void Clear()
        {
            lock (_lock)
                _root = new JObject();
        }

        // ── IDisposable ──────────────────────────────────────────────────

        /// <summary>释放资源并清空配置数据。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
                _root = null;
        }

        // ── 私有辅助 ─────────────────────────────────────────────────────

        private bool TryGetValue(string key, out JToken token)
        {
            if (!string.IsNullOrEmpty(key))
            {
                lock (_lock)
                {
                    if (_root != null && _root.TryGetValue(key, out token))
                        return true;
                }
            }
            token = null;
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ConfigManager));
        }
    }
}
