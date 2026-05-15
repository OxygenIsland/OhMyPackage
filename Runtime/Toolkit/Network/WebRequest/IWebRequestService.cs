// ============================================================
// IWebRequestService.cs
// 对标 GameFramework.WebRequest.IWebRequestManager。
//
// 核心差异：
//   GameFramework  → 事件回调（WebRequestStart / Success / Failure）
//   IWebRequestService → async UniTask，直接 await 获取结果，零回调
//
// 保留的 GameFramework 优秀思想：
//   ✓ 并发控制    MaxConcurrency ←→ TotalAgentCount
//   ✓ 优先级调度  WebRequestOptions.Priority ←→ AddWebRequest(priority)
//   ✓ 标签分组    WebRequestOptions.Tag ←→ AddWebRequest(tag)
//   ✓ 序列编号    WebRequestTaskInfo.SerialId ←→ TaskBase.SerialId
//   ✓ 任务快照    WebRequestTaskInfo ←→ TaskInfo
//   ✓ 统计属性    Active/Waiting/TotalRequestCount ←→ Working/Waiting/TotalAgentCount
//   ✓ 取消粒度    CancelRequest / CancelRequestsByTag / CancelAll
// ============================================================

using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求服务接口。提供 async/await 风格的 HTTP GET/POST，
    /// 内置并发限制、优先级调度、按 Tag 或 SerialId 取消等管理能力。
    /// </summary>
    public interface IWebRequestService
    {
        // ── 全局配置（对标 IWebRequestManager.Timeout / TotalAgentCount）──

        /// <summary>
        /// 最大并发请求数（默认 4）。
        /// 修改后立即对新入队任务生效，不影响已获得槽的运行中任务。
        /// 对标 GameFramework 通过多次调用 AddWebRequestAgentHelper 控制并发数。
        /// </summary>
        int MaxConcurrency { get; set; }

        /// <summary>
        /// 全局默认超时（秒）。当 <see cref="WebRequestOptions.Timeout"/> 为 0 时使用此值。
        /// 对标 <c>IWebRequestManager.Timeout</c>。
        /// </summary>
        int DefaultTimeout { get; set; }

        // ── 统计信息（对标 IWebRequestManager.WorkingAgentCount 等）──────

        /// <summary>当前正在执行（已获得并发槽）的请求数量。</summary>
        int ActiveRequestCount { get; }

        /// <summary>正在等待并发槽的请求数量。</summary>
        int WaitingRequestCount { get; }

        /// <summary>活跃 + 等待的总请求数量。</summary>
        int TotalRequestCount { get; }

        // ── HTTP 方法 ────────────────────────────────────────────────────

        /// <summary>
        /// 发起 GET 请求，将响应 JSON 反序列化为 <typeparamref name="T"/>。
        /// </summary>
        /// <param name="domain">域名，如 <c>https://api.example.com</c>。</param>
        /// <param name="path">路径，如 <c>/v1/user</c>。</param>
        /// <param name="options">可选配置（Tag / Priority / Timeout / Token / Headers / QueryParams）。</param>
        /// <param name="cancellationToken">调用方取消令牌（与服务内部令牌联合使用）。</param>
        UniTask<WebRequestResult<T>> GetAsync<T>(
            string domain,
            string path,
            WebRequestOptions options            = null,
            CancellationToken cancellationToken  = default);

        /// <summary>
        /// 发起 POST 请求，将 <paramref name="body"/> 序列化为 JSON body，
        /// 响应反序列化为 <typeparamref name="T"/>。
        /// </summary>
        UniTask<WebRequestResult<T>> PostAsync<T>(
            string domain,
            string path,
            object body,
            WebRequestOptions options            = null,
            CancellationToken cancellationToken  = default);

        /// <summary>
        /// 发起 POST 请求，使用原始字节 <paramref name="rawBody"/> 作为请求体
        /// （适合 Protobuf、自定义二进制协议等场景），响应反序列化为 <typeparamref name="T"/>。
        /// </summary>
        UniTask<WebRequestResult<T>> PostRawAsync<T>(
            string domain,
            string path,
            byte[] rawBody,
            WebRequestOptions options            = null,
            CancellationToken cancellationToken  = default);

        /// <summary>
        /// 发起 Multipart POST 请求（<c>multipart/form-data</c>），
        /// 适合文件上传、日志上报等场景。
        /// </summary>
        /// <param name="url">完整请求 URL。</param>
        /// <param name="formSections">表单分段数据。</param>
        /// <param name="options">可选配置（Tag / Priority / Timeout / Token）。</param>
        /// <param name="cancellationToken">调用方取消令牌。</param>
        UniTask<WebRequestResult<T>> PostMultipartAsync<T>(
            string url,
            List<IMultipartFormSection> formSections,
            WebRequestOptions options            = null,
            CancellationToken cancellationToken  = default);

        // ── 任务查询（对标 IWebRequestManager.GetWebRequestInfo）────────

        /// <summary>
        /// 根据序列编号获取任务信息快照。
        /// 不存在时返回 <see cref="WebRequestTaskInfo.Invalid"/>。
        /// 对标 <c>IWebRequestManager.GetWebRequestInfo(serialId)</c>。
        /// </summary>
        WebRequestTaskInfo GetTaskInfo(int serialId);

        /// <summary>
        /// 根据标签获取所有匹配任务的信息快照数组。
        /// 对标 <c>IWebRequestManager.GetWebRequestInfos(tag)</c>。
        /// </summary>
        WebRequestTaskInfo[] GetTaskInfosByTag(string tag);

        /// <summary>
        /// 根据标签填充任务信息快照列表（无数组分配，适合高频调用）。
        /// 对标 <c>IWebRequestManager.GetAllWebRequestInfos(tag, results)</c>。
        /// </summary>
        void GetTaskInfosByTag(string tag, List<WebRequestTaskInfo> results);

        // ── 取消控制（对标 IWebRequestManager.RemoveWebRequest）─────────

        /// <summary>
        /// 取消指定序列编号的请求（无论处于等待还是执行阶段）。
        /// 对标 <c>IWebRequestManager.RemoveWebRequest(serialId)</c>。
        /// </summary>
        /// <returns>找到任务并成功发出取消信号时返回 true。</returns>
        bool CancelRequest(int serialId);

        /// <summary>
        /// 取消指定标签的全部请求。
        /// 对标 <c>IWebRequestManager.RemoveWebRequests(tag)</c>。
        /// </summary>
        /// <returns>成功发出取消信号的请求数量。</returns>
        int CancelRequestsByTag(string tag);

        /// <summary>取消所有活跃与等待中的请求。</summary>
        void CancelAll();
    }
}
