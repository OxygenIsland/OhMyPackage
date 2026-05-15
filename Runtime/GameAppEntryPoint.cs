using System;
using GameFramework;
using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using NLog;
using VContainer.Unity;

namespace GameFramework
{
    /// <summary>
    /// 纯 C# 应用入口。
    /// 由 VContainer 在容器构建后自动启动，负责框架初始化、逐帧驱动与销毁清理。
    /// 无任何 MonoBehaviour 依赖；GameEntry 只作为 Bootstrap 桥接对象存在。
    /// </summary>
    public sealed class GameAppEntryPoint : IStartable, ITickable, IDisposable
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly IFsmManager m_FsmManager;
        private readonly IProcedureManager m_ProcedureManager;

        public GameAppEntryPoint(
            IFsmManager fsmManager,
            IProcedureManager procedureManager)
        {
            m_FsmManager = fsmManager;
            m_ProcedureManager = procedureManager;
        }

        public void Start()
        {
            StartProcedure();

            Log.Info("框架初始化完毕，应用启动。");
        }

        public void Tick()
        {
            GameFrameworkEntry.Update(UnityEngine.Time.deltaTime, UnityEngine.Time.unscaledDeltaTime);
        }

        public void Dispose()
        {
            Log.Info("框架已关闭。");
        }

        private void StartProcedure()
        {
            // IProcedureManager 内部持有 IFsm<IProcedureManager>，无需引用任何 MonoBehaviour。
            m_ProcedureManager.Initialize(
                m_FsmManager,
                new ProcedureLaunch(),
                new ProcedureCheckVersion(),
                new ProcedurePreload(),
                new ProcedureMain());

            m_ProcedureManager.StartProcedure<ProcedureLaunch>();

            Log.Info("流程状态机启动，当前流程：ProcedureLaunch。");
        }
    }
}