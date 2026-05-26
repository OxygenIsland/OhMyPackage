// ============================================================
//  MementoManager — Interfaces
//  调研来源：refactoring.guru / postsharp.net / codeproject
//  Generic Memento Pattern for Undo-Redo in C#
// ============================================================
using System;

namespace OhMyPackage.MementoManager
{
    // ── 快照对象接口 ──────────────────────────────────────────
    /// <summary>
    /// 备忘录快照。只暴露元数据，内部数据由 Originator 独占访问。
    /// </summary>
    public interface IMemento
    {
        /// <summary>快照唯一 Key（由 MementoManager 分配）</summary>
        string Key       { get; }

        /// <summary>可读描述，显示在 Undo 历史里</summary>
        string Label     { get; }

        /// <summary>创建时间戳</summary>
        DateTime CreatedAt { get; }

        /// <summary>版本号，每次 Save 自增</summary>
        int Version      { get; }
    }

    // ── 持有者接口 ────────────────────────────────────────────
    /// <summary>
    /// 任何需要被 MementoManager 管理状态的对象实现此接口。
    /// </summary>
    public interface IOriginator
    {
        /// <summary>创建并返回当前状态的快照</summary>
        IMemento SaveToMemento();

        /// <summary>从快照恢复状态</summary>
        void RestoreFromMemento(IMemento memento);
    }

    // ── 泛型持有者接口 ────────────────────────────────────────
    /// <summary>
    /// 强类型版本，State 为纯 C# 可序列化的数据类。
    /// 推荐在工具链中使用此接口，比反射/JSON 更安全。
    /// </summary>
    public interface IOriginator<TState> : IOriginator
        where TState : class, new()
    {
        TState CaptureState();
        void   ApplyState(TState state);
    }
}
