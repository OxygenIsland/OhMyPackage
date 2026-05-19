// -------------------------------------------------------
// MyGame — File Transfer Module (Upload)
// -------------------------------------------------------
using System;

namespace OhMyPackage.Download
{
    /// <summary>HTTP 上传方式。</summary>
    public enum UploadMethod
    {
        /// <summary>PUT 请求（幂等，适合单文件覆盖上传）。</summary>
        Put,
        /// <summary>POST 请求（适合表单或多次提交场景）。</summary>
        Post,
    }

    /// <summary>
    /// 单次上传任务的配置选项。
    /// </summary>
    public sealed class UploadOptions
    {
        /// <summary>单次请求超时时长（秒）。默认 60 秒。</summary>
        public float TimeoutSeconds { get; set; } = 60f;

        /// <summary>重试策略。默认使用指数退避重试 3 次。</summary>
        public RetryPolicy RetryPolicy { get; set; } = RetryPolicy.Default;

        /// <summary>请求 Content-Type。默认 application/octet-stream。</summary>
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>HTTP 上传方式（PUT 或 POST）。默认 PUT。</summary>
        public UploadMethod Method { get; set; } = UploadMethod.Put;

        /// <summary>进度回调。每次数据块发送后触发（主线程）。</summary>
        public IProgress<UploadProgress> Progress { get; set; } = null;

        /// <summary>透传的用户自定义数据，原样回传至 UploadResult.UserData。</summary>
        public object UserData { get; set; } = null;

        /// <summary>默认配置实例（每次 new，避免全局状态污染）。</summary>
        public static UploadOptions Default => new UploadOptions();
    }
}
