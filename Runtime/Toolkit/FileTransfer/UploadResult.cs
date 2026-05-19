// -------------------------------------------------------
// MyGame — File Transfer Module (Upload)
// -------------------------------------------------------
namespace OhMyPackage.Download
{
    /// <summary>
    /// 单次上传的最终结果（只读值类型）。
    /// 通过静态工厂方法创建，避免用异常传递预期失败。
    /// </summary>
    public readonly struct UploadResult
    {
        /// <summary>上传是否成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>是否被取消（用户或 CancellationToken 触发）。</summary>
        public bool IsCancelled { get; }

        /// <summary>上传目标 URI。</summary>
        public string Uri { get; }

        /// <summary>实际上传的字节数，失败时为 0。</summary>
        public long BytesSent { get; }

        /// <summary>服务器响应体文本（可能为 null 或空）。</summary>
        public string ResponseBody { get; }

        /// <summary>失败时的错误描述，成功时为 null。</summary>
        public string ErrorMessage { get; }

        /// <summary>调用方传入的自定义数据，原样回传。</summary>
        public object UserData { get; }

        private UploadResult(bool success, bool cancelled, string uri, long bytesSent,
            string responseBody, string error, object userData)
        {
            IsSuccess    = success;
            IsCancelled  = cancelled;
            Uri          = uri;
            BytesSent    = bytesSent;
            ResponseBody = responseBody;
            ErrorMessage = error;
            UserData     = userData;
        }

        public static UploadResult Success(string uri, long bytesSent, string responseBody = null, object userData = null)
            => new UploadResult(true, false, uri, bytesSent, responseBody, null, userData);

        public static UploadResult Failure(string error, string uri = null, object userData = null)
            => new UploadResult(false, false, uri, 0, null, error, userData);

        public static UploadResult Cancelled(string uri = null, object userData = null)
            => new UploadResult(false, true, uri, 0, null, "Cancelled", userData);

        public override string ToString() =>
            IsSuccess   ? $"Success: {Uri} ({BytesSent:N0} bytes sent)" :
            IsCancelled ? "Cancelled" :
                          $"Failure: {ErrorMessage}";
    }
}
