namespace MyGame.Toolkit.IO
{
    /// <summary>
    /// 文件加载结果封装，包含成功标志、数据和错误信息。
    /// <para>设计参考 Toolkit 层统一结果模型，保持项目内结果类型风格一致。</para>
    /// </summary>
    /// <typeparam name="T">加载后的数据类型。</typeparam>
    public readonly struct FileLoadResult<T>
    {
        /// <summary>是否加载成功。</summary>
        public bool Success { get; }

        /// <summary>加载的数据。加载失败时为 <c>default</c>。</summary>
        public T Data { get; }

        /// <summary>错误信息。加载成功时为 <c>null</c>。</summary>
        public string Error { get; }

        private FileLoadResult(bool success, T data, string error)
        {
            Success = success;
            Data = data;
            Error = error;
        }

        /// <summary>创建成功结果。</summary>
        public static FileLoadResult<T> Ok(T data) => new FileLoadResult<T>(true, data, null);

        /// <summary>创建失败结果。</summary>
        public static FileLoadResult<T> Fail(string error) => new FileLoadResult<T>(false, default, error);

        public void Deconstruct(out bool success, out T data, out string error)
        {
            success = Success;
            data = Data;
            error = Error;
        }
    }
}
