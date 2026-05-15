// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework.Download
{
    /// <summary>
    /// HTTP 下载后端接口（策略模式）。
    /// 通过替换实现可对接 UnityWebRequest、HttpClient 或 Mock 测试。
    /// </summary>
    public interface IDownloadHandler
    {
        /// <summary>
        /// 通过 HTTP HEAD 请求获取远程文件的完整大小（字节）。
        /// 服务器不支持时返回 -1，调用方需能处理未知大小的情况。
        /// </summary>
        UniTask<long> GetContentLengthAsync(string uri, CancellationToken ct = default);

        /// <summary>
        /// 将远程文件数据写入 <paramref name="destination"/> 流。
        /// <para>
        /// <paramref name="fromPosition"/> &gt; 0 时发起 HTTP Range 请求（断点续传）。
        /// </para>
        /// <para>
        /// <paramref name="onBytesReceived"/> 在每次收到并写入数据后回调，
        /// 参数为本次新增字节数，用于进度统计和速度计算。
        /// </para>
        /// </summary>
        UniTask DownloadToStreamAsync(
            string uri,
            Stream destination,
            long fromPosition,
            Action<int> onBytesReceived,
            CancellationToken ct = default);
    }
}
