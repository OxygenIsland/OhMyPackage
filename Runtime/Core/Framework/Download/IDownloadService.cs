// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework.Download
{
    /// <summary>
    /// 下载服务接口。
    /// <para>
    /// 所有下载方法均返回 <see cref="UniTask"/>，支持 async/await 线性编写，
    /// 无需拆分回调。失败通过 <see cref="DownloadResult"/> 返回，不抛出异常（取消除外）。
    /// </para>
    /// </summary>
    public interface IDownloadService
    {
        // ── 状态属性 ──────────────────────────────────────────────────

        /// <summary>当前实时下载速度（bytes/s）。</summary>
        float CurrentSpeedBytesPerSecond { get; }

        /// <summary>正在执行的下载任务数。</summary>
        int ActiveDownloadCount { get; }

        /// <summary>等待并发槽位的任务数。</summary>
        int PendingDownloadCount { get; }

        /// <summary>是否处于暂停状态。</summary>
        bool IsPaused { get; }

        // ── 控制方法 ──────────────────────────────────────────────────

        /// <summary>
        /// 设置最大并发下载数。在所有任务完成前调用时立即生效（新任务受新限制约束）。
        /// </summary>
        void SetMaxConcurrency(int maxConcurrency);

        /// <summary>
        /// 暂停下载。已在等待槽位或等待恢复的任务将阻塞直到 <see cref="Resume"/> 被调用。
        /// 正在进行的网络请求在本次请求结束后才受暂停影响。
        /// </summary>
        void Pause();

        /// <summary>恢复下载，唤醒所有等待暂停解除的任务。</summary>
        void Resume();

        // ── 下载方法 ──────────────────────────────────────────────────

        /// <summary>
        /// 异步下载单个文件。
        /// <para>
        /// 支持断点续传、超时、重试、进度回调、CancellationToken 取消。
        /// 返回的 <see cref="DownloadResult"/> 永远不会抛出异常；
        /// 取消时 <see cref="DownloadResult.IsCancelled"/> 为 true。
        /// </para>
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
        /// 批量异步下载。所有任务共享并发槽位，按提交顺序依次获取槽位。
        /// <para>
        /// <paramref name="batchProgress"/> 回调格式为 <c>(已完成数, 总数)</c>，
        /// 每完成一个任务触发一次（含失败/取消）。
        /// </para>
        /// </summary>
        UniTask<IReadOnlyList<DownloadResult>> DownloadBatchAsync(
            IEnumerable<(string uri, string savePath)> items,
            DownloadOptions sharedOptions = null,
            IProgress<(int completed, int total)> batchProgress = null,
            CancellationToken cancellationToken = default);
    }
}
