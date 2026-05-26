// ============================================================
//  MementoManager — HistoryChannel
//  调研来源：
//    codeproject.com "Generic Memento Pattern for Undo-Redo in C#"
//    → RoundStack 容量限制思路
//    codesociety.net → 双指针 List 实现 Undo/Redo
// ============================================================
using System;
using System.Collections.Generic;

namespace OhMyPackage.MementoManager
{
    /// <summary>
    /// 单个 Key 的历史通道：维护 Undo / Redo 两个栈。
    /// 容量由 maxCapacity 控制，超出时自动丢弃最老快照。
    /// </summary>
    internal sealed class HistoryChannel
    {
        private readonly int             _maxCapacity;
        private readonly List<IMemento>  _history  = new List<IMemento>();
        private int                      _cursor   = -1;   // 指向当前状态在 _history 中的索引

        internal string Key         { get; }
        internal bool   CanUndo     => _cursor > 0;
        internal bool   CanRedo     => _cursor < _history.Count - 1;
        internal int    Count       => _history.Count;
        internal int    CurrentIndex => _cursor;

        internal HistoryChannel(string key, int maxCapacity = 50)
        {
            Key           = key;
            _maxCapacity  = maxCapacity;
        }

        // ── Push：保存新快照 ──────────────────────────────────
        /// <summary>
        /// 向历史追加快照。
        /// 如果当前不在末尾（上次 Undo 之后又做了新操作），
        /// 先裁掉 cursor 之后的"未来"再追加——与 Git 行为一致。
        /// </summary>
        internal void Push(IMemento memento)
        {
            // 裁掉 cursor 之后的 redo 分支
            if (_cursor < _history.Count - 1)
                _history.RemoveRange(_cursor + 1, _history.Count - _cursor - 1);

            _history.Add(memento);

            // 超出容量：丢弃最旧
            if (_history.Count > _maxCapacity)
            {
                _history.RemoveAt(0);
                // cursor 相对位置保持，但上限收缩了
            }

            _cursor = _history.Count - 1;
        }

        // ── Undo ─────────────────────────────────────────────
        /// <summary>
        /// 返回 Undo 目标快照（cursor 回退一步），未改变时返回 null。
        /// </summary>
        internal IMemento Undo()
        {
            if (!CanUndo) return null;
            _cursor--;
            return _history[_cursor];
        }

        // ── Redo ─────────────────────────────────────────────
        internal IMemento Redo()
        {
            if (!CanRedo) return null;
            _cursor++;
            return _history[_cursor];
        }

        // ── Peek ─────────────────────────────────────────────
        internal IMemento Current()     => _cursor >= 0 ? _history[_cursor] : null;
        internal IMemento PeekUndo()    => CanUndo ? _history[_cursor - 1] : null;
        internal IMemento PeekRedo()    => CanRedo ? _history[_cursor + 1] : null;

        /// <summary>获取全部历史（只读视图）</summary>
        internal IReadOnlyList<IMemento> GetHistory() => _history.AsReadOnly();

        // ── 指定版本跳转 ──────────────────────────────────────
        /// <summary>
        /// 直接跳转到指定 version 的快照。
        /// 用于"跳转到某个存档点"场景。
        /// </summary>
        internal IMemento JumpToVersion(int version)
        {
            int idx = _history.FindIndex(m => m.Version == version);
            if (idx < 0) return null;
            _cursor = idx;
            return _history[_cursor];
        }

        internal void Clear()
        {
            _history.Clear();
            _cursor = -1;
        }
    }
}
