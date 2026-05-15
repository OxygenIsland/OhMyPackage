// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
using System;

namespace GameFramework.Download
{
    /// <summary>
    /// 单次下载任务的配置选项。
    /// </summary>
    public sealed class DownloadOptions
    {
        /// <summary>
        /// 任务优先级（数值越大越优先）。
        /// 当前实现中用于语义标记，实际调度为 FIFO；
        /// 如需严格优先级调度，可在业务层按优先级排序后再提交。
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>单次请求超时时长（秒）。默认 30 秒。</summary>
        public float TimeoutSeconds { get; set; } = 30f;

        /// <summary>重试策略。默认使用指数退避重试 3 次。</summary>
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

        /// <summary>是否启用断点续传（本地文件存在时从断点继续）。默认开启。</summary>
        public bool EnableResume { get; set; } = true;

        /// <summary>
        /// 期望的 MD5 校验值（十六进制小写字符串）。
        /// 不为 null 时，下载完成后自动校验，失败则删除文件并抛出异常。
        /// </summary>
        public string ExpectedMD5 { get; set; } = null;

        /// <summary>进度回调。每次收到数据块后触发（主线程）。</summary>
        public IProgress<DownloadProgress> Progress { get; set; } = null;

        /// <summary>透传的用户自定义数据，原样回传至 DownloadResult.UserData。</summary>
        public object UserData { get; set; } = null;

        /// <summary>默认配置实例（每次 new，避免全局状态污染）。</summary>
        public static DownloadOptions Default => new DownloadOptions();
    }
}
