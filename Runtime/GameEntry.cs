// ===================================================================
// GameEntry.cs  （Unity 层 · 常驻桥接器）
//
// 职责：
//  1. 作为 Launch 场景中的唯一 Bootstrap MonoBehaviour
//  2. 保证 Bootstrap 节点跨场景存活
//  3. 初始化日志等必须依赖 Unity 生命周期的基础设施
//
// 非职责：
//  · 不直接创建流程 FSM
//  · 不直接驱动 GameFramework.Update
//  · 不直接负责容器中服务的启动与销毁
//
// 标准版架构：
//  RootLifetimeScope -> RegisterEntryPoint<GameAppEntryPoint>()
//                   -> 纯 C# EntryPoint 启动框架
//  GameEntry       -> 仅作为 Unity 桥接对象与 FSM Owner
// ===================================================================

using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 启动桥接器。
    /// 挂载在 Launch 场景中的 Bootstrap GameObject 上。
    /// </summary>
    public sealed class GameEntry : MonoBehaviour
    {
        private void Awake()
        {
            // Bootstrap 节点整体跨场景存活，RootLifetimeScope 与其子容器一并保留。
            DontDestroyOnLoad(gameObject);

            // 日志初始化依赖 Unity 运行时环境，放在最早的 Awake 阶段处理。
            InitLogSystem();
        }

        /// <summary>
        /// 初始化日志系统。
        /// </summary>
        private void InitLogSystem()
        {
            NLogManager.Initialize();
        }
    }
}
