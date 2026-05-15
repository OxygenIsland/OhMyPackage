// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
namespace GameFramework.Download
{
    /// <summary>
    /// 单次下载的最终结果（只读值类型）。
    /// 通过静态工厂方法创建，避免使用异常来传递预期失败。
    /// </summary>
    public readonly struct DownloadResult
    {
        /// <summary>下载是否成功。</summary>
        public bool IsSuccess { get; }

        /// <summary>是否被取消（用户或 CancellationToken 触发）。</summary>
        public bool IsCancelled { get; }

        /// <summary>本地保存路径（失败时可能为 null）。</summary>
        public string SavePath { get; }

        /// <summary>下载完成后的文件大小（字节），失败时为 0。</summary>
        public long FileSize { get; }

        /// <summary>失败时的错误描述，成功时为 null。</summary>
        public string ErrorMessage { get; }

        /// <summary>调用方传入的自定义数据，原样回传。</summary>
        public object UserData { get; }

        private DownloadResult(bool success, bool cancelled, string savePath, long fileSize, string error, object userData)
        {
            IsSuccess   = success;
            IsCancelled = cancelled;
            SavePath    = savePath;
            FileSize    = fileSize;
            ErrorMessage = error;
            UserData    = userData;
        }

        public static DownloadResult Success(string savePath, long fileSize, object userData = null)
            => new DownloadResult(true, false, savePath, fileSize, null, userData);

        public static DownloadResult Failure(string error, string savePath = null, object userData = null)
            => new DownloadResult(false, false, savePath, 0, error, userData);

        public static DownloadResult Cancelled(string savePath = null, object userData = null)
            => new DownloadResult(false, true, savePath, 0, "Cancelled", userData);

        public override string ToString() =>
            IsSuccess   ? $"Success: {SavePath} ({FileSize:N0} bytes)" :
            IsCancelled ? "Cancelled" :
                          $"Failure: {ErrorMessage}";
    }
}
