// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GameFramework.Download
{
    /// <summary>
    /// 基于 <see cref="UnityWebRequest"/> 的默认 HTTP 下载处理器。
    /// <para>
    /// 使用自定义 <see cref="StreamingDownloadHandlerScript"/> 将数据直接流式写入
    /// 目标 <see cref="Stream"/>，全程零拷贝，不在内存中额外缓存整个文件。
    /// </para>
    /// </summary>
    public sealed class UnityWebRequestDownloadHandler : IDownloadHandler
    {
        /// <summary>
        /// 发起 HTTP HEAD 请求获取完整文件大小。
        /// 服务器不支持 HEAD 或无 Content-Length 时返回 -1。
        /// </summary>
        public async UniTask<long> GetContentLengthAsync(string uri, CancellationToken ct = default)
        {
            using var req = UnityWebRequest.Head(uri);
            try
            {
                await req.SendWebRequest().WithCancellation(ct);
            }
            catch
            {
                return -1L;
            }

            if (req.result != UnityWebRequest.Result.Success)
                return -1L;

            return long.TryParse(req.GetResponseHeader("Content-Length"), out long length)
                ? length
                : -1L;
        }

        /// <summary>
        /// 发起 HTTP GET 请求，将数据流式写入 <paramref name="destination"/>。
        /// <paramref name="fromPosition"/> &gt; 0 时添加 <c>Range: bytes=N-</c> 请求头实现断点续传。
        /// </summary>
        public async UniTask DownloadToStreamAsync(
            string uri,
            Stream destination,
            long fromPosition,
            Action<int> onBytesReceived,
            CancellationToken ct = default)
        {
            using var req = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET);

            if (fromPosition > 0)
                req.SetRequestHeader("Range", $"bytes={fromPosition}-");

            req.downloadHandler = new StreamingDownloadHandlerScript(destination, onBytesReceived);

            await req.SendWebRequest().WithCancellation(ct);

            if (req.result == UnityWebRequest.Result.ConnectionError
                || req.result == UnityWebRequest.Result.ProtocolError
                || req.result == UnityWebRequest.Result.DataProcessingError)
            {
                throw new Exception($"HTTP {req.responseCode}: {req.error} — {uri}");
            }
        }
    }

    /// <summary>
    /// 自定义 <see cref="DownloadHandlerScript"/>，将 Unity 收到的每块数据直接写入
    /// 目标 <see cref="Stream"/>，彻底避免 Unity 在内存中为大文件建立副本。
    ///
    /// <para>
    /// 向基类构造函数传入预分配缓冲区（64 KB）可以进一步减少 GC 压力。
    /// </para>
    /// </summary>
    internal sealed class StreamingDownloadHandlerScript : DownloadHandlerScript
    {
        private readonly Stream      _destination;
        private readonly Action<int> _onBytesReceived;

        public StreamingDownloadHandlerScript(Stream destination, Action<int> onBytesReceived)
            : base(new byte[65536]) // 预分配 64 KB 复用缓冲区
        {
            _destination     = destination;
            _onBytesReceived = onBytesReceived;
        }

        // Unity 每次收到网络数据块时调用（主线程）
        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0) return true;
            _destination.Write(data, 0, dataLength);
            _onBytesReceived?.Invoke(dataLength);
            return true;
        }

        // 禁用 Unity 内部的内存缓存，避免二次存储
        protected override byte[] GetData()     => null;
        protected override string GetText()     => null;
        protected override void CompleteContent() { }
        protected override float GetProgress()  => 0f;
    }
}
