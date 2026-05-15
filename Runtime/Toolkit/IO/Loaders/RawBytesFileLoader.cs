using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace MyGame.Toolkit.IO
{
    /// <summary>
    /// 原始字节文件加载器，将本地文件读取为 <c>byte[]</c>。
    /// <para>适用于需要获取文件二进制内容的场景，如自定义格式解析、二进制协议数据等。</para>
    /// </summary>
    public sealed class RawBytesFileLoader : IFileLoader<byte[]>
    {
        /// <inheritdoc />
        public FileLoadResult<byte[]> Load(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return FileLoadResult<byte[]>.Fail("文件路径不能为空。");
            if (!File.Exists(filePath))
                return FileLoadResult<byte[]>.Fail($"文件不存在：{filePath}");

            try
            {
                var data = File.ReadAllBytes(filePath);
                return FileLoadResult<byte[]>.Ok(data);
            }
            catch (Exception ex)
            {
                return FileLoadResult<byte[]>.Fail($"读取字节文件失败：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public async UniTask<FileLoadResult<byte[]>> LoadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return FileLoadResult<byte[]>.Fail("文件路径不能为空。");
            if (!File.Exists(filePath))
                return FileLoadResult<byte[]>.Fail($"文件不存在：{filePath}");

            try
            {
                var data = await UniTask.RunOnThreadPool(
                    () => File.ReadAllBytes(filePath),
                    cancellationToken: cancellationToken);
                return FileLoadResult<byte[]>.Ok(data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return FileLoadResult<byte[]>.Fail($"读取字节文件失败：{ex.Message}");
            }
        }
    }
}
