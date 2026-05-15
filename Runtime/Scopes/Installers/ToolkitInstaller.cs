using MyGame.Toolkit.DiskCache;
using MyGame.Toolkit.IO;
using MyGame.Toolkit.Network;
using VContainer;
using VContainer.Unity;

namespace MyGame.Scopes.Installers
{
    /// <summary>
    /// 注册 Toolkit 层的所有基础设施服务（DiskCache、Network、IO 等）。
    /// 所有服务均以 Singleton 注册在根容器中，跨场景共享。
    /// </summary>
    public sealed class ToolkitInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            // ── DiskCache ──────────────────────────────────────────────
            // FileCacheConfig 是纯数据对象，用 RegisterInstance 注入已构建的实例。
            // 如需自定义目录或容量限制，在此处修改 Builder 配置。
            builder.RegisterInstance(new DiskCacheConfig.Builder()
                .SetMaxCacheBytes(500 * 1024 * 1024) // 示例：限制 500 MB
                .Build());

            // IFileHashProvider → MD5FileHashProvider（无状态，Singleton 安全）
            builder.Register<IFileHashProvider, MD5FileHashProvider>(Lifetime.Singleton);

            // IDiskCache → FileCacheManager（Dispose 由容器自动调用）
            builder.Register<IDiskCache, DiskCacheManager>(Lifetime.Singleton);

            // ── IO（本地文件加载器）─────────────────────────────────────
            // IFileLoader<string> → TextFileLoader（默认 UTF-8 编码）
            builder.Register<IFileLoader<string>, TextFileLoader>(Lifetime.Singleton);

            // IFileLoader<byte[]> → RawBytesFileLoader（无状态，Singleton 安全）
            builder.Register<IFileLoader<byte[]>, RawBytesFileLoader>(Lifetime.Singleton);

            // IJsonFileLoader → JsonFileLoader（默认 UTF-8 + Newtonsoft 默认设置）
            builder.Register<IJsonFileLoader, JsonFileLoader>(Lifetime.Singleton);

            // ── Network（Web 请求服务）──────────────────────────────────────
            // IWebRequestService → WebRequestService（并发调度 + 优先级 + 取消管理）
            // 容器销毁时自动调用 Dispose() 触发 CancelAll()。
            builder.Register<IWebRequestService, WebRequestService>(Lifetime.Singleton);
        }
    }
}
