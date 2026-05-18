using System;
using UnityEngine;
using VContainer.Unity;

namespace OhMyPackage
{
    /// <summary>
    /// 调试器管理器。
    /// new DebuggerWindowGroup() 无外部依赖，字段初始化即可，无需 IStartable。
    /// ITickable  → 需要 RegisterEntryPoint 才会被容器驱动每帧调用。
    /// IDisposable → 无需 EntryPoint，容器销毁时自动调用。
    /// </summary>
    internal sealed partial class DebuggerModule : IDebuggerModule, ITickable, IDisposable
    {
        // 无外部依赖 → 字段初始化，构造完成即可用，不需要 IStartable
        private readonly DebuggerWindowGroup _debuggerWindowRoot = new DebuggerWindowGroup();
        private bool _activeWindow;

        /// <summary>
        /// 获取或设置调试器窗口是否激活。
        /// </summary>
        public bool ActiveWindow
        {
            get => _activeWindow;
            set => _activeWindow = value;
        }

        /// <summary>
        /// 调试器窗口根结点。
        /// </summary>
        public IDebuggerWindowGroup DebuggerWindowRoot => _debuggerWindowRoot;

        /// <summary>
        /// 调试器管理器轮询。
        /// </summary>
        public void Tick()
        {
            if (!_activeWindow)
            {
                return;
            }

            _debuggerWindowRoot.OnUpdate();
        }

        /// <summary>
        /// 关闭并清理调试器管理器。
        /// </summary>
        public void Dispose()
        {
            _activeWindow = false;
            _debuggerWindowRoot.Shutdown();
        }

        /// <summary>
        /// 注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <param name="debuggerWindow">要注册的调试器窗口。</param>
        /// <param name="args">初始化调试器窗口参数。</param>
        public void RegisterDebuggerWindow(string path, IDebuggerWindow debuggerWindow, params object[] args)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new OhMyPackageException("Path is invalid.");
            }

            if (debuggerWindow == null)
            {
                throw new OhMyPackageException("Debugger window is invalid.");
            }

            _debuggerWindowRoot.RegisterDebuggerWindow(path, debuggerWindow);
            debuggerWindow.Initialize(args);
        }

        /// <summary>
        /// 解除注册调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否解除注册调试器窗口成功。</returns>
        public bool UnregisterDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.UnregisterDebuggerWindow(path);
        }

        /// <summary>
        /// 获取调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>要获取的调试器窗口。</returns>
        public IDebuggerWindow GetDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.GetDebuggerWindow(path);
        }

        /// <summary>
        /// 选中调试器窗口。
        /// </summary>
        /// <param name="path">调试器窗口路径。</param>
        /// <returns>是否成功选中调试器窗口。</returns>
        public bool SelectDebuggerWindow(string path)
        {
            return _debuggerWindowRoot.SelectDebuggerWindow(path);
        }
    }
}