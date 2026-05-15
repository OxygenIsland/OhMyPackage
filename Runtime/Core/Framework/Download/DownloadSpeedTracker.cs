// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System.Collections.Generic;
using System.Diagnostics;

namespace GameFramework.Download
{
    /// <summary>
    /// 滑动时间窗口下载速度计算器（线程安全）。
    /// 通过记录每次收到数据的时间戳，计算最近 N 秒内的平均速度。
    /// </summary>
    internal sealed class DownloadSpeedTracker
    {
        private readonly float _windowSeconds;
        private readonly LinkedList<(long timestampMs, long bytes)> _samples;
        private readonly Stopwatch _stopwatch;
        private readonly object _lock = new object();
        private long _windowBytes;

        /// <summary>当前计算出的下载速度（bytes/s）。</summary>
        public float CurrentSpeed { get; private set; }

        /// <param name="windowSeconds">滑动窗口大小（秒）。默认 3 秒。</param>
        public DownloadSpeedTracker(float windowSeconds = 3f)
        {
            _windowSeconds = windowSeconds;
            _samples       = new LinkedList<(long, long)>();
            _stopwatch     = Stopwatch.StartNew();
        }

        /// <summary>
        /// 记录新增字节数，同时更新当前速度。
        /// 可在任意线程调用。
        /// </summary>
        public void AddBytes(long bytes)
        {
            lock (_lock)
            {
                long nowMs = _stopwatch.ElapsedMilliseconds;
                _samples.AddLast((nowMs, bytes));
                _windowBytes += bytes;
                Prune(nowMs);
                CurrentSpeed = _windowSeconds > 0f ? _windowBytes / _windowSeconds : 0f;
            }
        }

        /// <summary>清空历史采样，将速度归零。</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _samples.Clear();
                _windowBytes  = 0;
                CurrentSpeed = 0f;
            }
        }

        // 移除超出窗口的过期采样
        private void Prune(long nowMs)
        {
            long cutoffMs = nowMs - (long)(_windowSeconds * 1000f);
            while (_samples.Count > 0 && _samples.First.Value.timestampMs < cutoffMs)
            {
                _windowBytes -= _samples.First.Value.bytes;
                _samples.RemoveFirst();
            }
        }
    }
}
