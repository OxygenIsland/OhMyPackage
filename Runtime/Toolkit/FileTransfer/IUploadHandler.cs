// -------------------------------------------------------
// MyGame — File Transfer Module (Upload)
// -------------------------------------------------------
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace OhMyPackage.Download
{
    /// <summary>
    /// HTTP 上传后端接口（策略模式）。
    /// 通过替换实现可对接 UnityWebRequest、HttpClient 或 Mock 测试。
    /// </summary>
    public interface IUploadHandler
    {
        /// <summary>
        /// 将本地文件上传至指定 URI。
        /// <para>
        /// 上传过程中通过 <paramref name="onBytesSent"/> 回调通知进度，
        /// 参数为 (已发送字节数, 文件总字节数)。
        /// </para>
        /// </summary>
        /// <param name="uri">上传目标地址。</param>
        /// <param name="localPath">本地文件路径。</param>
        /// <param name="options">上传选项（内容类型、HTTP 方法、超时等）。</param>
        /// <param name="onBytesSent">进度回调，参数为 (bytesSent, totalBytes)。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>服务器响应体文本（可能为 null 或空）。</returns>
        UniTask<string> UploadFileAsync(
            string uri,
            string localPath,
            UploadOptions options,
            Action<long, long> onBytesSent,
            CancellationToken ct = default);
    }
}
