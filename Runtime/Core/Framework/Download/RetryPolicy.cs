// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System;

namespace GameFramework.Download
{
    /// <summary>
    /// 下载失败重试策略。
    /// </summary>
    public sealed class RetryPolicy
    {
        /// <summary>最大重试次数（不含首次尝试）。默认 3 次。</summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>首次重试前的等待秒数。默认 1 秒。</summary>
        public float InitialDelaySeconds { get; set; } = 1f;

        /// <summary>是否启用指数退避（每次重试延迟翻倍）。默认开启。</summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>退避延迟的最大上限（秒）。默认 30 秒。</summary>
        public float MaxDelaySeconds { get; set; } = 30f;

        /// <summary>默认策略：最多重试 3 次，指数退避。</summary>
        public static RetryPolicy Default => new RetryPolicy();

        /// <summary>无重试策略。</summary>
        public static RetryPolicy NoRetry => new RetryPolicy { MaxRetries = 0 };

        /// <summary>计算第 attempt 次重试前应等待的秒数（attempt 从 0 开始）。</summary>
        public float GetDelaySeconds(int attempt)
        {
            float delay = UseExponentialBackoff
                ? InitialDelaySeconds * (float)Math.Pow(2.0, attempt)
                : InitialDelaySeconds;

            return Math.Min(delay, MaxDelaySeconds);
        }
    }
}
