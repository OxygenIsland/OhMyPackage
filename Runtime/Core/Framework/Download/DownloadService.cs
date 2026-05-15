// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework.Download
{
    /// <summary>
    /// 下载服务核心实现（纯 C# 类，不依赖 MonoBehaviour）。
    ///
    /// 架构亮点：
    /// <list type="bullet">
    ///   <item>async/await 全程线性代码，无回调分裂</item>
    ///   <item>SemaphoreSlim 控制并发，PauseGate 支持暂停/恢复</item>
    ///   <item>内置指数退避重试、超时、断点续传、可选 MD5 校验</item>
    ///   <item>IDownloadHandler 策略接口解耦 HTTP 后端，便于测试</item>
    /// </list>
    /// </summary>
    public sealed class DownloadService : IDownloadService, IDisposable
    {
        private readonly IDownloadHandler      _handler;
        private readonly DownloadSpeedTracker  _speedTracker;
        private readonly CancellationTokenSource _disposeCts;

        private SemaphoreSlim                  _semaphore;
        private volatile UniTaskCompletionSource _pauseGate;
        private volatile bool                  _isPaused;
        private int                            _activeCount;
        private int                            _pendingCount;
        private bool                           _disposed;

        // ── 公开属性 ─────────────────────────────────────────────────

        public float CurrentSpeedBytesPerSecond => _speedTracker.CurrentSpeed;
        public int   ActiveDownloadCount        => _activeCount;
        public int   PendingDownloadCount       => _pendingCount;
        public bool  IsPaused                   => _isPaused;

        // ── 构造 ──────────────────────────────────────────────────────

        /// <param name="handler">HTTP 下载后端（注入 UnityWebRequestDownloadHandler 或自定义实现）。</param>
        /// <param name="maxConcurrency">最大并发下载数，默认 3。</param>
        public DownloadService(IDownloadHandler handler, int maxConcurrency = 3)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Must be > 0.");

            _handler      = handler ?? throw new ArgumentNullException(nameof(handler));
            _speedTracker = new DownloadSpeedTracker();
            _disposeCts   = new CancellationTokenSource();
            _semaphore    = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            // 初始状态：未暂停，Gate 已完成（开放通行）
            _pauseGate = new UniTaskCompletionSource();
            _pauseGate.TrySetResult();
        }

        // ── 控制方法 ─────────────────────────────────────────────────

        public void SetMaxConcurrency(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Must be > 0.");
            var old = _semaphore;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            old.Dispose();
        }

        /// <summary>
        /// 暂停：新任务在获取槽位前会阻塞，直到 Resume() 被调用。
        /// 已在传输中的网络请求完成本次 ReceiveData 后才会在下一个任务边界感知暂停。
        /// </summary>
        public void Pause()
        {
            if (_isPaused) return;
            _isPaused  = true;
            _pauseGate = new UniTaskCompletionSource(); // 新的"关闭"门
        }

        public void Resume()
        {
            if (!_isPaused) return;
            _isPaused = false;
            _pauseGate.TrySetResult(); // 打开当前门，唤醒所有等待者
        }

        // ── 核心下载方法 ──────────────────────────────────────────────

        public async UniTask<DownloadResult> DownloadAsync(
            string uri,
            string savePath,
            DownloadOptions options           = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            options ??= DownloadOptions.Default;

            // 合并外部 CancellationToken 与 Dispose 令牌
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disposeCts.Token);
            var ct = linkedCts.Token;

            Interlocked.Increment(ref _pendingCount);
            try
            {
                // 1. 等待暂停解除
                await _pauseGate.Task.AttachExternalCancellation(ct);

                // 2. 等待并发槽位（FIFO；如需优先级调度请在业务层按 Priority 排序后提交）
                await _semaphore.WaitAsync(ct);

                Interlocked.Decrement(ref _pendingCount);
                Interlocked.Increment(ref _activeCount);
                try
                {
                    return await ExecuteWithRetryAsync(uri, savePath, options, ct);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeCount);
                    _semaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Interlocked.Decrement(ref _pendingCount);
                return DownloadResult.Cancelled(savePath, options.UserData);
            }
        }

        public async UniTask<IReadOnlyList<DownloadResult>> DownloadBatchAsync(
            IEnumerable<(string uri, string savePath)> items,
            DownloadOptions sharedOptions                    = null,
            IProgress<(int completed, int total)> batchProgress = null,
            CancellationToken cancellationToken              = default)
        {
            ThrowIfDisposed();
            var list = new List<(string uri, string savePath)>(items);
            int total = list.Count;
            if (total == 0) return Array.Empty<DownloadResult>();

            var results        = new DownloadResult[total];
            int completedCount = 0;
            var tasks          = new UniTask<DownloadResult>[total];

            for (int i = 0; i < total; i++)
            {
                int idx = i;
                tasks[idx] = TrackItem(idx);
            }

            await UniTask.WhenAll(tasks);
            return results;

            // 本地函数：下载并更新批进度
            async UniTask<DownloadResult> TrackItem(int index)
            {
                var (uri, path) = list[index];
                var result = await DownloadAsync(uri, path, sharedOptions, cancellationToken);
                results[index] = result;
                // completedCount 在 UniTask 主线程调度下无竞态，++ 足够安全
                batchProgress?.Report((++completedCount, total));
                return result;
            }
        }

        // ── 内部实现 ─────────────────────────────────────────────────

        private async UniTask<DownloadResult> ExecuteWithRetryAsync(
            string uri, string savePath, DownloadOptions options, CancellationToken ct)
        {
            var policy    = options.RetryPolicy ?? RetryPolicy.NoRetry;
            string lastError = null;

            for (int attempt = 0; attempt <= policy.MaxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    float delay = policy.GetDelaySeconds(attempt - 1);
                    await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: ct);

                    // 重试前再次检查暂停状态
                    await _pauseGate.Task.AttachExternalCancellation(ct);

                    Debug.Log($"[DownloadService] Retry {attempt}/{policy.MaxRetries}: {uri}");
                }

                try
                {
                    return await ExecuteSingleDownloadAsync(uri, savePath, options, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // 用户主动取消，不重试，直接向上传播
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (attempt < policy.MaxRetries)
                        Debug.LogWarning($"[DownloadService] Attempt {attempt + 1} failed for {uri}: {ex.Message}");
                }
            }

            Debug.LogError($"[DownloadService] All attempts failed for {uri}: {lastError}");
            return DownloadResult.Failure(lastError, savePath, options.UserData);
        }

        private async UniTask<DownloadResult> ExecuteSingleDownloadAsync(
            string uri, string savePath, DownloadOptions options, CancellationToken ct)
        {
            // 确保目标目录存在
            string dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // ① 超时令牌（独立于外部 ct，仅作用于本次网络请求）
            using var timeoutCts  = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var downloadCt = downloadCts.Token;

            // ② 获取远程文件总大小（HEAD 请求，不携带 Range，结果为完整文件大小）
            long totalBytes = await _handler.GetContentLengthAsync(uri, downloadCt);

            // ③ 断点续传：检查本地已有文件
            long startPosition = 0L;
            if (options.EnableResume && File.Exists(savePath))
            {
                long existingSize = new FileInfo(savePath).Length;

                if (totalBytes > 0 && existingSize >= totalBytes)
                {
                    // 文件已完整，直接校验 MD5（如配置）后返回
                    return await VerifyAndReturnAsync(savePath, existingSize, totalBytes,
                        options, ct);
                }

                startPosition = existingSize; // 从断点续传
            }

            // ④ 打开 FileStream（Append 或 Create）
            using var fs = new FileStream(
                savePath,
                startPosition > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 65536,
                useAsync:   true);

            long currentBytes = startPosition;

            void OnBytesReceived(int count)
            {
                currentBytes += count;
                _speedTracker.AddBytes(count);
                options.Progress?.Report(new DownloadProgress(
                    uri, savePath, currentBytes, totalBytes, _speedTracker.CurrentSpeed));
            }

            // ⑤ 执行下载，流式写入 FileStream
            await _handler.DownloadToStreamAsync(uri, fs, startPosition, OnBytesReceived, downloadCt);

            // ⑥ 最终 Flush（使用外部 ct，确保 Dispose 时也能完成）
            await fs.FlushAsync(ct);
            long finalSize = fs.Length;

            // ⑦ 可选 MD5 完整性校验
            if (!string.IsNullOrEmpty(options.ExpectedMD5))
            {
                string actual = await ComputeMD5Async(savePath, ct);
                if (!string.Equals(actual, options.ExpectedMD5, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(savePath);
                    throw new InvalidDataException(
                        $"MD5 mismatch for {uri}. Expected: {options.ExpectedMD5}, Actual: {actual}");
                }
            }

            return DownloadResult.Success(savePath, finalSize, options.UserData);
        }

        // 文件已存在且完整时，校验 MD5 后直接返回 Success
        private async UniTask<DownloadResult> VerifyAndReturnAsync(
            string savePath, long existingSize, long totalBytes,
            DownloadOptions options, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(options.ExpectedMD5))
            {
                string actual = await ComputeMD5Async(savePath, ct);
                if (!string.Equals(actual, options.ExpectedMD5, StringComparison.OrdinalIgnoreCase))
                {
                    // MD5 不符，删除后重新下载
                    File.Delete(savePath);
                    throw new InvalidDataException(
                        $"Existing file MD5 mismatch. Expected: {options.ExpectedMD5}, Actual: {actual}");
                }
            }

            Debug.Log($"[DownloadService] File already complete, skipping: {savePath}");
            return DownloadResult.Success(savePath, existingSize, options.UserData);
        }

        // 在线程池中计算 MD5，避免阻塞主线程
        private static async UniTask<string> ComputeMD5Async(string filePath, CancellationToken ct)
        {
            return await UniTask.RunOnThreadPool(() =>
            {
                using var md5 = MD5.Create();
                using var fs  = File.OpenRead(filePath);
                byte[] hash   = md5.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }, cancellationToken: ct);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DownloadService));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposeCts.Cancel();
            _disposeCts.Dispose();
            _semaphore?.Dispose();
        }
    }
}
