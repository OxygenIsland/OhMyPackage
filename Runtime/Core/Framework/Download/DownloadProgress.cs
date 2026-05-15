// -------------------------------------------------------
// MyGame — Download Module (Modern / UniTask)
// -------------------------------------------------------
namespace GameFramework.Download
{
    /// <summary>
    /// 单个文件的下载进度快照（只读值类型，可安全传递给任意线程）。
    /// </summary>
    public readonly struct DownloadProgress
    {
        /// <summary>下载来源 URI。</summary>
        public string DownloadUri { get; }

        /// <summary>本地保存路径。</summary>
        public string SavePath { get; }

        /// <summary>当前已下载字节数（含断点续传起始量）。</summary>
        public long CurrentBytes { get; }

        /// <summary>文件总字节数。服务器未返回 Content-Length 时为 -1。</summary>
        public long TotalBytes { get; }

        /// <summary>当前下载速度（bytes/s）。</summary>
        public float SpeedBytesPerSecond { get; }

        /// <summary>下载进度 [0, 1]。TotalBytes 未知时返回 0。</summary>
        public float Percentage => TotalBytes > 0 ? (float)CurrentBytes / TotalBytes : 0f;

        /// <summary>是否已知文件总大小。</summary>
        public bool IsTotalKnown => TotalBytes > 0;

        public DownloadProgress(string uri, string savePath, long currentBytes, long totalBytes, float speed)
        {
            DownloadUri = uri;
            SavePath = savePath;
            CurrentBytes = currentBytes;
            TotalBytes = totalBytes;
            SpeedBytesPerSecond = speed;
        }
    }
}
