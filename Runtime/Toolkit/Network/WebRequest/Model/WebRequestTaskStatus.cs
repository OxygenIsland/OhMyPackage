// ============================================================
// WebRequestTaskStatus.cs
// 对标 GameFramework.WebRequest.WebRequestManager.WebRequestTaskStatus。
// ============================================================

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求任务的生命周期状态。
    /// </summary>
    public enum WebRequestTaskStatus : byte
    {
        /// <summary>等待执行（在优先级队列中排队）。对标 GameFramework 的 Todo。</summary>
        Waiting = 0,

        /// <summary>执行中（已获得并发槽，正在发送 HTTP 请求）。对标 GameFramework 的 Doing。</summary>
        Running = 1,

        /// <summary>已成功完成。对标 GameFramework 的 Done。</summary>
        Succeeded = 2,

        /// <summary>执行失败（网络错误或服务端错误）。对标 GameFramework 的 Error。</summary>
        Failed = 3,

        /// <summary>已取消（通过 CancellationToken 或主动调用 Cancel 触发）。</summary>
        Cancelled = 4,
    }
}
