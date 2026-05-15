// ===================================================================
// RootLifetimeScope.cs  （Composition Root · 全局根容器）
//
// 职责：
//  1. 作为整个游戏的 DI 组合根（Composition Root）
//  2. 向 VContainer 注册所有全局单例服务
//  3. 注册纯 C# EntryPoint，统一启动与销毁应用生命周期
//  4. 将场景中已存在的 GameEntry MonoBehaviour 纳入容器管理
//
// 挂载要求：
//  · 与 GameEntry 挂载于 Launch 场景的同一个 GameObject
//  · DontDestroyOnLoad 由 GameEntry.Awake() 负责，两者同节点即可
//
// 容器层级：
//  RootLifetimeScope (全局，跨场景存活)
//    └── SceneLifetimeScope (可选，各场景独立子容器)
// ===================================================================

using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using MyGame.Scopes.Installers;
using VContainer;
using VContainer.Unity;

namespace GameFramework
{
    /// <summary>
    /// 根级生命周期作用域（全局唯一）
    /// 负责注册框架核心模块，供 GameEntry 及后续所有业务模块注入
    /// </summary>
    public sealed class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // ── 框架核心模块（Singleton）────────────────────────────────
            // 使用工厂委托包装 GameFrameworkEntry 的懒加载机制
            // VContainer 保证每个接口在容器生命周期内只实例化一次
            builder.Register<IFsmManager>(
                _ => GameFrameworkEntry.GetModule<IFsmManager>(),
                Lifetime.Singleton);


            builder.Register<IProcedureManager>(
                _ => GameFrameworkEntry.GetModule<IProcedureManager>(),
                Lifetime.Singleton);

            // ── Toolkit 基础设施模块 ───────────────────────────────────
            new ToolkitInstaller().Install(builder);

            // ── Config 模块 ────────────────────────────────────────────
            new ConfigInstaller().Install(builder);

            // ── 注入场景中已存在的 MonoBehaviour ──────────────────────
            // GameEntry 仅作为 Unity 桥接器存在，不再是 FSM Owner。
            builder.RegisterComponentInHierarchy<GameEntry>();

            // ── Runtime 日志上报门面 ───────────────────────────────────
            builder.Register<ILogUploadService, LogUploadFacade>(Lifetime.Singleton);

            // ── 标准版应用入口 ───────────────────────────────────────
            // 由纯 C# EntryPoint 负责应用初始化、逐帧驱动与销毁清理。
            builder.RegisterEntryPoint<GameAppEntryPoint>();
        }
    }
}
