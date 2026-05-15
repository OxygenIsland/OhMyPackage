using System.Threading;
using Cysharp.Threading.Tasks;

namespace MyGame.Toolkit.IO
{
    /// <summary>
    /// 本地文件加载器的通用接口。
    /// <para>每种固定返回类型的加载器对应一个 <typeparamref name="T"/> 的具体实现，例如：
    /// <list type="bullet">
    ///   <item><c>IFileLoader&lt;string&gt;</c> — 纯文本加载（<see cref="TextFileLoader"/>）。</item>
    ///   <item><c>IFileLoader&lt;byte[]&gt;</c> — 原始字节加载（<see cref="RawBytesFileLoader"/>）。</item>
    /// </list>
    /// 对于需要泛型反序列化的场景（如 JSON → 任意类型），请使用 <see cref="IJsonFileLoader"/>。
    /// </para>
    /// </summary>
    /// <typeparam name="T">加载后的数据类型。</typeparam>
    public interface IFileLoader<T>
    {
        /// <summary>
        /// 同步加载本地文件。
        /// </summary>
        /// <param name="filePath">文件绝对路径。</param>
        /// <returns>加载结果。</returns>
        FileLoadResult<T> Load(string filePath);

        /// <summary>
        /// 异步加载本地文件。IO 操作在线程池执行，不阻塞主线程。
        /// </summary>
        /// <param name="filePath">文件绝对路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载结果。</returns>
        UniTask<FileLoadResult<T>> LoadAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
