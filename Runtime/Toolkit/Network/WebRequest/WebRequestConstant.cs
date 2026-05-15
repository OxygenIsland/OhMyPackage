// ============================================================
// WebRequestConstant.cs
// 对标 GameFramework.WebRequest.Constant。
// ============================================================

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求模块内部常量。
    /// </summary>
    internal static class WebRequestConstant
    {
        /// <summary>默认任务优先级（数值越大越优先）。</summary>
        internal const int DefaultPriority = 0;

        /// <summary>全局默认超时（秒）。</summary>
        internal const int DefaultTimeout = 30;

        /// <summary>默认最大并发请求数（对标 GameFramework 的 Agent 总数）。</summary>
        internal const int DefaultMaxConcurrency = 4;
    }
}
