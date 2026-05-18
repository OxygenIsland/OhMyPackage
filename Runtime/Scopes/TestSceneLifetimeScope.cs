// ===================================================================
// TestSceneLifetimeScope.cs  （测试场景子容器）
//
// 职责：
//  1. 作为测试场景专属的 DI 子容器
//  2. 继承 RootLifetimeScope 中所有全局服务
//  3. 允许通过重新注册接口来替换为 Mock/Stub（测试替身）
//  4. 场景卸载时自动销毁，不影响根容器
//
// 挂载要求：
//  · 挂载于测试场景的任意 GameObject
//  · 在 Inspector 中将 ParentReference 指向 RootLifetimeScope（或留空自动查找）
//
// VContainer Scope 树：
//  RootLifetimeScope  (全局)
//    └── TestSceneLifetimeScope  (测试场景，仅在测试场景存活)
//
// 覆盖注册规则（行业惯例）：
//  · 子 Scope 中重新 Register 同一接口 → 子 Scope 内优先使用新实现
//  · 父 Scope 中已注册的服务不受影响
//  · 适合注入 FakeXxx / MockXxx / StubXxx 类型
// ===================================================================

using OhMyPackage.Scopes.Installers;
using VContainer;
using VContainer.Unity;

namespace OhMyPackage.Scopes
{
    /// <summary>
    /// 测试场景生命周期作用域。
    /// 继承根容器全局服务，可按需覆盖注册测试替身。
    /// </summary>
    public sealed class TestSceneLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // ── 测试专属服务注册 ──────────────────────────────────────
            // 在此注册测试场景独有的服务，或用 Fake/Mock 覆盖父容器实现。
            // 支持“仅加载测试场景”时独立运行：确保 IDebuggerModule 等基础依赖可解析。
            builder.RegisterEntryPoint<DebuggerModule>().As<IDebuggerModule>();

            // Debugger 是 MonoBehaviour，需通过此方式纳入容器，VContainer 才会调用 [Inject] 方法。
            builder.RegisterComponentInHierarchy<Debugger>();
        }
    }
}
