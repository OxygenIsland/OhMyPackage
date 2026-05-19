// -------------------------------------------------------
// MyGame — File Transfer Module (Upload)
// -------------------------------------------------------
namespace OhMyPackage.Download
{
    /// <summary>
    /// 单个文件的上传进度快照（只读值类型，可安全传递给任意线程）。
    /// </summary>
    public readonly struct UploadProgress
    {
        /// <summary>本地源文件路径。</summary>
        public string LocalPath { get; }

        /// <summary>上传目标 URI。</summary>
        public string Uri { get; }

        /// <summary>当前已上传字节数。</summary>
        public long BytesSent { get; }

        /// <summary>文件总字节数。</summary>
        public long TotalBytes { get; }

        /// <summary>当前上传速度（bytes/s）。</summary>
        public float SpeedBytesPerSecond { get; }

        /// <summary>上传进度 [0, 1]。TotalBytes 为 0 时返回 0。</summary>
        public float Percentage => TotalBytes > 0 ? (float)BytesSent / TotalBytes : 0f;

        /// <summary>是否已知文件总大小。</summary>
        public bool IsTotalKnown => TotalBytes > 0;

        public UploadProgress(string localPath, string uri, long bytesSent, long totalBytes, float speed)
        {
            LocalPath           = localPath;
            Uri                 = uri;
            BytesSent           = bytesSent;
            TotalBytes          = totalBytes;
            SpeedBytesPerSecond = speed;
        }
    }
}
