// ============================================================
// WebRequestOptions.cs
// 对标 GameFramework 中 AddWebRequest 各重载散落的参数，
// 将它们集中到一个可选配置对象中，保持调用处整洁。
// ============================================================

using System.Collections.Generic;

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求的可选配置项，可在每次请求时独立指定。
    /// <para>
    /// 所有字段均有默认值；只需设置与默认值不同的字段。
    /// 对应关系：<br/>
    ///   <see cref="Tag"/>      → GameFramework AddWebRequest(tag)      按组管理与取消<br/>
    ///   <see cref="Priority"/> → GameFramework AddWebRequest(priority)  并发槽优先级调度<br/>
    ///   <see cref="Timeout"/>  → GameFramework IWebRequestManager.Timeout 超时（秒）<br/>
    ///   <see cref="Token"/>    → Authorization Bearer Token<br/>
    ///   <see cref="Headers"/>  → 附加 HTTP 请求头<br/>
    ///   <see cref="QueryParams"/> → URL 查询字符串参数
    /// </para>
    /// </summary>
    public sealed class WebRequestOptions
    {
        /// <summary>
        /// 任务标签。同组请求可通过
        /// <see cref="IWebRequestService.CancelRequestsByTag"/> 批量取消，
        /// 也可通过 <see cref="IWebRequestService.GetTaskInfosByTag"/> 统一查询。
        /// 空字符串表示无标签。
        /// </summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>
        /// 任务优先级，数值越大越优先（默认 0）。
        /// 当并发槽已满时，高优先级任务优先获得下一个空闲槽；
        /// 同优先级按提交先后（FIFO）排队。
        /// </summary>
        public int Priority { get; set; } = WebRequestConstant.DefaultPriority;

        /// <summary>
        /// 请求超时时长（秒）。
        /// 0 或负数表示使用 <see cref="IWebRequestService.DefaultTimeout"/>。
        /// </summary>
        public int Timeout { get; set; } = 0;

        /// <summary>
        /// Bearer Token。不为空时自动添加 <c>Authorization: Bearer {Token}</c> 请求头。
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>附加 HTTP 请求头（可为 null）。</summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// URL 查询字符串参数（可为 null）。
        /// 会自动 URL 编码并追加到 URI 末尾，例如 <c>?key=value&amp;foo=bar</c>。
        /// </summary>
        public Dictionary<string, string> QueryParams { get; set; }
    }
}
