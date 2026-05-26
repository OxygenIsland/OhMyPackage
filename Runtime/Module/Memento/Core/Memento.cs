// ============================================================
//  MementoManager — Concrete Memento
//  泛型快照：State 保存真实数据，外部只见 IMemento 接口
// ============================================================
using System;

namespace OhMyPackage.MementoManager
{
    /// <summary>
    /// 内部快照实现。只有同 Key 的 IOriginator 可以拆包 State。
    /// 外部（Caretaker）仅持有 IMemento 引用，无法直接读取 State，
    /// 保证了封装性（GoF 备忘录模式核心约束）。
    /// </summary>
    internal sealed class Memento<TState> : IMemento
        where TState : class, new()
    {
        // ── IMemento ──────────────────────────────────────────
        public string   Key       { get; }
        public string   Label     { get; }
        public DateTime CreatedAt { get; }
        public int      Version   { get; }

        // ── 内部访问 ──────────────────────────────────────────
        /// <summary>
        /// 只有 MementoManager 内部（通过 internal 访问）可以读取。
        /// 状态数据对外完全隐藏。
        /// </summary>
        internal TState State { get; }

        internal Memento(string key, string label, TState state, int version)
        {
            Key       = key;
            Label     = label;
            State     = state;
            Version   = version;
            CreatedAt = DateTime.Now;
        }

        public override string ToString() =>
            $"[{Key}] v{Version} \"{Label}\" @ {CreatedAt:HH:mm:ss}";
    }
}
