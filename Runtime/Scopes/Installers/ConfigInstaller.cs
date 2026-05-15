using GameFramework;
using VContainer;
using VContainer.Unity;

namespace MyGame.Scopes.Installers
{
    /// <summary>
    /// 向 VContainer 注册 Config 模块服务。
    /// <para>
    /// <see cref="ConfigManager"/> 以 Singleton 注册，容器销毁时自动调用 <c>Dispose()</c>。
    /// 业务代码通过构造函数注入 <see cref="IConfigManager"/>，无需关心具体实现。
    /// </para>
    ///
    /// <example>
    /// <code>
    /// // 在 RootLifetimeScope.Configure 中调用：
    /// new ConfigInstaller().Install(builder);
    ///
    /// // 业务侧注入：
    /// public sealed class MyService
    /// {
    ///     private readonly IConfigManager _config;
    ///     public MyService(IConfigManager config) { _config = config; }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    public sealed class ConfigInstaller : IInstaller
    {
        /// <inheritdoc />
        public void Install(IContainerBuilder builder)
        {
            builder.Register<IConfigManager, ConfigManager>(Lifetime.Singleton);
        }
    }
}
