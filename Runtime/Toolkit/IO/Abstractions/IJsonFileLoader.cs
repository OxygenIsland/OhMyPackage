using System.Threading;
using Cysharp.Threading.Tasks;

namespace MyGame.Toolkit.IO
{
    /// <summary>
    /// JSON 文件加载器接口。
    /// <para>与 <see cref="IFileLoader{T}"/> 不同，此接口的泛型参数位于方法级别，
    /// 允许同一个加载器实例将 JSON 反序列化为任意目标类型。</para>
    /// <para>默认实现为 <see cref="JsonFileLoader"/>，基于 Newtonsoft.Json。</para>
    /// </summary>
    public interface IJsonFileLoader
    {
        /// <summary>
        /// 同步加载 JSON 文件并反序列化为 <typeparamref name="T"/>。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="filePath">文件绝对路径。</param>
        /// <returns>加载结果。</returns>
        FileLoadResult<T> Load<T>(string filePath);

        /// <summary>
        /// 异步加载 JSON 文件并反序列化为 <typeparamref name="T"/>。IO 操作在线程池执行。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="filePath">文件绝对路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>加载结果。</returns>
        UniTask<FileLoadResult<T>> LoadAsync<T>(string filePath, CancellationToken cancellationToken = default);
    }
}
