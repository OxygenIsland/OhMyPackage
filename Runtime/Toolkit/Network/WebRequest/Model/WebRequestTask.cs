// ============================================================
// WebRequestTask.cs
// 对标 GameFramework.WebRequest.WebRequestManager.WebRequestTask。
// 持有单次请求的全部元信息与取消令牌对。
//
// 取消令牌设计（对标 GameFramework Agent/Task 分离）：
//   TaskCts   ── 任务独立令牌，由 CancelRequest(serialId) 触发取消
//   LinkedCts ── 联合令牌：TaskCts + 调用方 CancellationToken
//                传递给底层 HTTP 调用，任一方取消均能终止请求
// ============================================================

using System;
using System.Threading;

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求任务内部数据容器（仅供 <see cref="WebRequestService"/> 使用）。
    /// 对标 GameFramework 中的 WebRequestTask。
    /// </summary>
    internal sealed class WebRequestTask : IDisposable
    {
        private static int s_SerialSeed = 0;

        // ── 元信息 ────────────────────────────────────────────────────────

        /// <summary>全局唯一序列编号，从 1 起递增。对标 GameFramework TaskBase.SerialId。</summary>
        public int SerialId { get; }

        /// <summary>请求完整 URI（domain + path）。</summary>
        public string Uri { get; }

        /// <summary>任务标签，空字符串表示无标签。</summary>
        public string Tag { get; }

        /// <summary>优先级，数值越大越优先。</summary>
        public int Priority { get; }

        /// <summary>请求超时（秒，已解析为最终值）。</summary>
        public int Timeout { get; }

        /// <summary>创建时间（UTC）。</summary>
        public DateTime CreateTime { get; }

        // ── 状态 ──────────────────────────────────────────────────────────

        /// <summary>当前任务状态。由 WebRequestService 写入。</summary>
        public WebRequestTaskStatus Status { get; set; }

        /// <summary>
        /// 是否已成功获取并发槽。
        /// <para>
        /// 用于 <see cref="WebRequestService"/> 判断任务被取消时是否需要归还槽位：
        /// 若任务在等待队列中被取消（从未获得槽），则不应执行 Release 逻辑。
        /// </para>
        /// </summary>
        public bool SlotAcquired { get; set; }

        // ── 取消令牌 ──────────────────────────────────────────────────────

        /// <summary>
        /// 任务独立 CTS。调用 <see cref="IWebRequestService.CancelRequest"/> 时取消此源。
        /// </summary>
        public CancellationTokenSource TaskCts { get; }

        /// <summary>
        /// 联合 CTS（TaskCts + 调用方 CancellationToken）。
        /// 传递给底层 HTTP 调用，任一方取消均终止请求。
        /// </summary>
        public CancellationTokenSource LinkedCts { get; }

        // ── 构造 ──────────────────────────────────────────────────────────

        internal WebRequestTask(
            string uri,
            WebRequestOptions options,
            int resolvedTimeout,
            CancellationTokenSource taskCts,
            CancellationTokenSource linkedCts)
        {
            SerialId    = Interlocked.Increment(ref s_SerialSeed);
            Uri         = uri;
            Tag         = options?.Tag ?? string.Empty;
            Priority    = options?.Priority ?? WebRequestConstant.DefaultPriority;
            Timeout     = resolvedTimeout;
            Status      = WebRequestTaskStatus.Waiting;
            CreateTime  = DateTime.UtcNow;
            SlotAcquired = false;
            TaskCts     = taskCts;
            LinkedCts   = linkedCts;
        }

        // ── 工具方法 ──────────────────────────────────────────────────────

        /// <summary>生成当前状态的只读快照（对标 GameFramework TaskInfo）。</summary>
        public WebRequestTaskInfo ToInfo() =>
            new WebRequestTaskInfo(SerialId, Uri, Tag, Priority, Status, CreateTime);

        /// <summary>释放两个 CancellationTokenSource。</summary>
        public void Dispose()
        {
            LinkedCts.Dispose();
            TaskCts.Dispose();
        }
    }
}
