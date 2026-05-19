// -------------------------------------------------------
// MyGame — File Transfer Module (Download + Upload)
// -------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace OhMyPackage.Download
{
    /// <summary>
    /// 文件传输服务接口，统一管理文件下载与上传。
    /// <para>
    /// 所有传输方法均返回 <see cref="UniTask"/>，支持 async/await 线性编写，无需拆分回调。
    /// 失败通过结果值类型返回，不抛出异常（取消除外）。
    /// </para>
    /// </summary>
    public interface IFileTransferService
    {
        // ── 下载状态 ──────────────────────────────────────────────────

        /// <summary>当前实时下载速度（bytes/s）。</summary>
        float CurrentDownloadSpeedBytesPerSecond { get; }

        /// <summary>正在执行的下载任务数。</summary>
        int ActiveDownloadCount { get; }

        /// <summary>等待并发槽位的下载任务数。</summary>
        int PendingDownloadCount { get; }

        /// <summary>是否处于全局暂停状态（同时影响下载和上传）。</summary>
        bool IsPaused { get; }

        // ── 下载控制 ──────────────────────────────────────────────────

        /// <summary>设置最大并发下载数，立即对新入队任务生效。</summary>
        void SetMaxDownloadConcurrency(int maxConcurrency);

        /// <summary>
        /// 暂停所有传输。等待槽位的任务将阻塞直到 <see cref="Resume"/> 被调用。
        /// 正在进行的网络请求在本次数据块完成后才感知暂停。
        /// </summary>
        void Pause();

        /// <summary>恢复所有传输，唤醒所有等待暂停解除的任务。</summary>
        void Resume();

        // ── 下载方法 ──────────────────────────────────────────────────

        /// <summary>
        /// 异步下载单个文件。
        /// <para>支持断点续传、超时、重试、进度回调、CancellationToken 取消。</para>
        /// </summary>
        /// <param name="uri">远程文件地址。</param>
        /// <param name="savePath">本地保存路径（目录不存在时自动创建）。</param>
        /// <param name="options">下载选项，传 null 使用默认配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<DownloadResult> DownloadAsync(
            string uri,
            string savePath,
            DownloadOptions options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量异步下载。所有任务共享下载并发槽位，按提交顺序获取槽位。
        /// <para><paramref name="batchProgress"/> 格式为 <c>(已完成数, 总数)</c>，每完成一个触发一次。</para>
        /// </summary>
        UniTask<IReadOnlyList<DownloadResult>> DownloadBatchAsync(
            IEnumerable<(string uri, string savePath)> items,
            DownloadOptions sharedOptions = null,
            IProgress<(int completed, int total)> batchProgress = null,
            CancellationToken cancellationToken = default);

        // ── 上传状态 ──────────────────────────────────────────────────

        /// <summary>当前实时上传速度（bytes/s）。</summary>
        float CurrentUploadSpeedBytesPerSecond { get; }

        /// <summary>正在执行的上传任务数。</summary>
        int ActiveUploadCount { get; }

        /// <summary>等待并发槽位的上传任务数。</summary>
        int PendingUploadCount { get; }

        // ── 上传控制 ──────────────────────────────────────────────────

        /// <summary>设置最大并发上传数，立即对新入队任务生效。</summary>
        void SetMaxUploadConcurrency(int maxConcurrency);

        // ── 上传方法 ──────────────────────────────────────────────────

        /// <summary>
        /// 异步上传单个本地文件。
        /// <para>支持超时、重试、进度回调、CancellationToken 取消。</para>
        /// </summary>
        /// <param name="localPath">本地源文件路径。</param>
        /// <param name="uri">上传目标地址。</param>
        /// <param name="options">上传选项，传 null 使用默认配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        UniTask<UploadResult> UploadAsync(
            string localPath,
            string uri,
            UploadOptions options = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量异步上传。所有任务共享上传并发槽位，按提交顺序获取槽位。
        /// <para><paramref name="batchProgress"/> 格式为 <c>(已完成数, 总数)</c>，每完成一个触发一次。</para>
        /// </summary>
        UniTask<IReadOnlyList<UploadResult>> UploadBatchAsync(
            IEnumerable<(string localPath, string uri)> items,
            UploadOptions sharedOptions = null,
            IProgress<(int completed, int total)> batchProgress = null,
            CancellationToken cancellationToken = default);
    }
}
