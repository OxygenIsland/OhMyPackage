using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MyGame.Toolkit.IO
{
    /// <summary>
    /// 纯文本文件加载器，将本地文件读取为 <see cref="string"/>。
    /// <para>默认使用 UTF-8 编码，可通过构造函数指定其他编码。
    /// 由 VContainer 通过构造函数注入。</para>
    /// </summary>
    public sealed class TextFileLoader : IFileLoader<string>
    {
        private readonly Encoding _encoding;

        /// <summary>使用 UTF-8 编码创建加载器。</summary>
        public TextFileLoader() : this(Encoding.UTF8) { }

        /// <summary>使用指定编码创建加载器。</summary>
        /// <param name="encoding">文件编码。</param>
        public TextFileLoader(Encoding encoding)
        {
            _encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
        }

        /// <inheritdoc />
        public FileLoadResult<string> Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return FileLoadResult<string>.Fail("文件路径不能为空。");
            if (!File.Exists(filePath))
                return FileLoadResult<string>.Fail($"文件不存在：{filePath}");

            try
            {
                var content = File.ReadAllText(filePath, _encoding);
                return FileLoadResult<string>.Ok(content);
            }
            catch (Exception ex)
            {
                return FileLoadResult<string>.Fail($"读取文本文件失败：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public async UniTask<FileLoadResult<string>> LoadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return FileLoadResult<string>.Fail("文件路径不能为空。");
            if (!File.Exists(filePath))
                return FileLoadResult<string>.Fail($"文件不存在：{filePath}");

            try
            {
                var encoding = _encoding;
                var content = await UniTask.RunOnThreadPool(
                    () => File.ReadAllText(filePath, encoding),
                    cancellationToken: cancellationToken);
                return FileLoadResult<string>.Ok(content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return FileLoadResult<string>.Fail($"读取文本文件失败：{ex.Message}");
            }
        }
    }
}
