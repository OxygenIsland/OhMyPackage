// ============================================================
// WebRequestResult.cs
// Web 请求结果顶层公开类型，
// 供 IWebRequestService 作为统一返回模型使用。
// ============================================================

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// HTTP 请求结果封装，包含成功标志、反序列化数据与错误信息。
    /// 为值类型（readonly struct），可零分配传递。
    /// </summary>
    public readonly struct WebRequestResult<T>
    {
        public bool   Success { get; }
        public T      Data    { get; }
        public string Error   { get; }

        private WebRequestResult(bool success, T data, string error)
        {
            Success = success;
            Data    = data;
            Error   = error;
        }

        /// <summary>构造成功结果。</summary>
        public static WebRequestResult<T> Ok(T data)       => new WebRequestResult<T>(true,  data,    null);

        /// <summary>构造失败结果。</summary>
        public static WebRequestResult<T> Fail(string error) => new WebRequestResult<T>(false, default, error);

        /// <summary>解构支持：<c>var (ok, data, err) = result;</c></summary>
        public void Deconstruct(out bool success, out T data, out string error)
        {
            success = Success;
            data    = Data;
            error   = Error;
        }
    }
}
