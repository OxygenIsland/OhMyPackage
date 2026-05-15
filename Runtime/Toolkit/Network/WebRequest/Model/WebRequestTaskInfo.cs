// ============================================================
// WebRequestTaskInfo.cs
// 对标 GameFramework 中的 TaskInfo：提供任务状态的只读快照，
// 可安全跨帧传递而不持有对内部 WebRequestTask 的引用。
// ============================================================

using System;

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求任务信息快照（只读值类型）。
    /// 通过 <see cref="IWebRequestService.GetTaskInfo"/> 或
    /// <see cref="IWebRequestService.GetTaskInfosByTag"/> 获取。
    /// </summary>
    public readonly struct WebRequestTaskInfo
    {
        /// <summary>无效任务信息的哨兵值（SerialId == 0）。</summary>
        public static readonly WebRequestTaskInfo Invalid = default;

        /// <summary>任务序列编号（全局唯一，从 1 递增）。</summary>
        public int SerialId { get; }

        /// <summary>请求完整 URI。</summary>
        public string Uri { get; }

        /// <summary>任务标签，用于分组查询与批量取消。空字符串表示无标签。</summary>
        public string Tag { get; }

        /// <summary>优先级，数值越大优先级越高。</summary>
        public int Priority { get; }

        /// <summary>当前任务状态。</summary>
        public WebRequestTaskStatus Status { get; }

        /// <summary>任务创建时间（UTC）。</summary>
        public DateTime CreateTime { get; }

        /// <summary>SerialId 大于 0 时为有效快照。</summary>
        public bool IsValid => SerialId > 0;

        internal WebRequestTaskInfo(
            int serialId, string uri, string tag,
            int priority, WebRequestTaskStatus status, DateTime createTime)
        {
            SerialId   = serialId;
            Uri        = uri;
            Tag        = tag;
            Priority   = priority;
            Status     = status;
            CreateTime = createTime;
        }
    }
}
