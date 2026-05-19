// -------------------------------------------------------
// MyGame — File Transfer Module (Upload)
// -------------------------------------------------------
using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace OhMyPackage.Download
{
    /// <summary>
    /// 基于 <see cref="UnityWebRequest"/> 的默认 HTTP 上传处理器。
    /// <para>
    /// 在线程池中读取文件为 byte[]，再通过 <see cref="UploadHandlerRaw"/> 发送。
    /// 轮询 <see cref="UnityWebRequest.uploadProgress"/> 以提供进度回调。
    /// </para>
    /// </summary>
    public sealed class UnityWebRequestUploadHandler : IUploadHandler
    {
        /// <inheritdoc/>
        public async UniTask<string> UploadFileAsync(
            string uri,
            string localPath,
            UploadOptions options,
            Action<long, long> onBytesSent,
            CancellationToken ct = default)
        {
            // ① 在线程池中读取文件，避免主线程阻塞
            byte[] data = await UniTask.RunOnThreadPool(
                () => File.ReadAllBytes(localPath),
                cancellationToken: ct);

            long totalBytes = data.Length;

            string method = options.Method == UploadMethod.Put
                ? UnityWebRequest.kHttpVerbPUT
                : UnityWebRequest.kHttpVerbPOST;

            using var req = new UnityWebRequest(uri, method);

            var uploadHandler = new UploadHandlerRaw(data) { contentType = options.ContentType };
            req.uploadHandler   = uploadHandler;
            req.downloadHandler = new DownloadHandlerBuffer(); // 捕获服务器响应

            // ② 超时令牌
            using var timeoutCts  = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var linkedCts   = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var uploadCt = linkedCts.Token;

            var operation = req.SendWebRequest();

            // ③ 轮询上传进度（UnityWebRequest 不提供流式回调，以 Yield 间隔轮询）
            while (!operation.isDone)
            {
                uploadCt.ThrowIfCancellationRequested();
                long sent = (long)(req.uploadProgress * totalBytes);
                onBytesSent?.Invoke(sent, totalBytes);
                await UniTask.Yield(PlayerLoopTiming.Update, uploadCt);
            }

            // ④ 最终进度 100%
            onBytesSent?.Invoke(totalBytes, totalBytes);

            if (req.result == UnityWebRequest.Result.ConnectionError
                || req.result == UnityWebRequest.Result.ProtocolError
                || req.result == UnityWebRequest.Result.DataProcessingError)
            {
                throw new Exception($"Upload failed [{req.responseCode}]: {req.error} — {uri}");
            }

            return req.downloadHandler?.text;
        }
    }
}
