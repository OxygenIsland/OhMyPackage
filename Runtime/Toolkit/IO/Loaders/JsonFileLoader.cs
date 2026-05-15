using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace MyGame.Toolkit.IO
{
    /// <summary>
    /// JSON 文件加载器，将本地 JSON 文件反序列化为指定类型。
    /// <para>基于 Newtonsoft.Json，默认使用 UTF-8 编码读取文件。
    /// 可通过构造函数自定义编码和 <see cref="JsonSerializerSettings"/>。
    /// 由 VContainer 通过构造函数注入。</para>
    ///
    /// <example>
    /// <code>
    /// // 通过 DI 注入
    /// public class MyService
    /// {
    ///     private readonly IJsonFileLoader _jsonLoader;
    ///     public MyService(IJsonFileLoader jsonLoader) { _jsonLoader = jsonLoader; }
    ///
    ///     public async UniTask LoadConfig()
    ///     {
    ///         var result = await _jsonLoader.LoadAsync&lt;AppConfig&gt;(filePath);
    ///         if (result.Success) { /* 使用 result.Data */ }
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    public sealed class JsonFileLoader : IJsonFileLoader
    {
        private readonly Encoding _encoding;
        private readonly JsonSerializerSettings _settings;

        /// <summary>使用默认配置（UTF-8、默认序列化设置）创建加载器。</summary>
        public JsonFileLoader() : this(Encoding.UTF8, null) { }

        /// <summary>使用指定编码和序列化设置创建加载器。</summary>
        /// <param name="encoding">文件编码。传 <c>null</c> 则使用 UTF-8。</param>
        /// <param name="settings">JSON 序列化设置。传 <c>null</c> 则使用 Newtonsoft 默认设置。</param>
        public JsonFileLoader(Encoding encoding, JsonSerializerSettings settings = null)
        {
            _encoding = encoding ?? Encoding.UTF8;
            _settings = settings;
        }

        /// <inheritdoc />
        public FileLoadResult<T> Load<T>(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return FileLoadResult<T>.Fail("文件路径不能为空。");
            if (!File.Exists(filePath))
                return FileLoadResult<T>.Fail($"文件不存在：{filePath}");

            try
            {
                var json = File.ReadAllText(filePath, _encoding);
                var data = _settings != null
                    ? JsonConvert.DeserializeObject<T>(json, _settings)
                    : JsonConvert.DeserializeObject<T>(json);
                return FileLoadResult<T>.Ok(data);
            }
            catch (JsonException ex)
            {
                return FileLoadResult<T>.Fail($"JSON 反序列化失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                return FileLoadResult<T>.Fail($"读取 JSON 文件失败：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public async UniTask<FileLoadResult<T>> LoadAsync<T>(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return FileLoadResult<T>.Fail("文件路径不能为空。");
            if (!File.Exists(filePath))
                return FileLoadResult<T>.Fail($"文件不存在：{filePath}");

            try
            {
                var encoding = _encoding;
                var settings = _settings;
                var data = await UniTask.RunOnThreadPool(() =>
                {
                    var json = File.ReadAllText(filePath, encoding);
                    return settings != null
                        ? JsonConvert.DeserializeObject<T>(json, settings)
                        : JsonConvert.DeserializeObject<T>(json);
                }, cancellationToken: cancellationToken);
                return FileLoadResult<T>.Ok(data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (JsonException ex)
            {
                return FileLoadResult<T>.Fail($"JSON 反序列化失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                return FileLoadResult<T>.Fail($"读取 JSON 文件失败：{ex.Message}");
            }
        }
    }
}
