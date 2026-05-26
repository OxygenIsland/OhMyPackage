// ============================================================
//  MementoManager — MementoOriginator<TState>
//  便捷基类：业务类继承后只需实现 CaptureState / ApplyState
//
//  使用方式：
//    public class MapEditor : MementoOriginator<MapEditorState>
//    {
//        public override MapEditorState CaptureState() => new MapEditorState { ... };
//        public override void ApplyState(MapEditorState s) { ... }
//    }
//    // 然后直接：
//    this.SaveSnapshot("MapEditor", "修改地形");
//    this.UndoSnapshot("MapEditor");
// ============================================================
using UnityEngine;

namespace OhMyPackage.MementoManager
{
    /// <summary>
    /// MonoBehaviour 基类版本。
    /// 继承此类后自动实现 IOriginator&lt;TState&gt;，
    /// 并提供 SaveSnapshot / UndoSnapshot / RedoSnapshot 快捷方法。
    /// </summary>
    public abstract class MementoOriginator<TState> : MonoBehaviour, IOriginator<TState>
        where TState : class, new()
    {
        // ── IOriginator<TState> ───────────────────────────────
        public abstract TState CaptureState();
        public abstract void   ApplyState(TState state);

        // ── IOriginator（非泛型，供 Manager 内部统一调用）─────
        IMemento IOriginator.SaveToMemento()
        {
            var state   = CaptureState();
            var copy    = StateSerializer.DeepCopy(state);
            var version = MementoManager.Instance != null
                ? (int)(System.DateTime.Now.Ticks % 100000)
                : 0;
            return new Memento<TState>(GetType().Name, "", copy, version);
        }

        void IOriginator.RestoreFromMemento(IMemento memento)
        {
            if (memento is Memento<TState> typed)
                ApplyState(StateSerializer.DeepCopy(typed.State));
            else
                Debug.LogWarning($"[MementoOriginator] 类型不匹配: {memento?.GetType()}");
        }

        // ── 快捷方法 ──────────────────────────────────────────
        /// <summary>保存当前状态到指定 Key</summary>
        public IMemento SaveSnapshot(string key, string label = "")
        {
            EnsureManager();
            return MementoManager.Instance.Save(key, this, label);
        }

        /// <summary>Undo 到上一步并自动应用</summary>
        public bool UndoSnapshot(string key)
        {
            EnsureManager();
            return MementoManager.Instance.Undo(key, this);
        }

        /// <summary>Redo 到下一步并自动应用</summary>
        public bool RedoSnapshot(string key)
        {
            EnsureManager();
            return MementoManager.Instance.Redo(key, this);
        }

        private static void EnsureManager()
        {
            if (MementoManager.Instance == null)
                Debug.LogError("[MementoOriginator] MementoManager 未初始化！");
        }
    }

    // ── 纯 C# 非 MonoBehaviour 版本 ───────────────────────────
    /// <summary>
    /// 普通 C# 类版本（不依赖 MonoBehaviour）。
    /// 适合编辑器工具类、纯逻辑层对象。
    /// </summary>
    public abstract class MementoOriginatorBase<TState> : IOriginator<TState>
        where TState : class, new()
    {
        public abstract TState CaptureState();
        public abstract void   ApplyState(TState state);

        IMemento IOriginator.SaveToMemento()
        {
            var copy    = StateSerializer.DeepCopy(CaptureState());
            return new Memento<TState>(GetType().Name, "", copy, 0);
        }

        void IOriginator.RestoreFromMemento(IMemento memento)
        {
            if (memento is Memento<TState> typed)
                ApplyState(StateSerializer.DeepCopy(typed.State));
        }

        public IMemento SaveSnapshot(string key, string label = "")
            => MementoManager.Instance.Save(key, this, label);

        public bool UndoSnapshot(string key)
            => MementoManager.Instance.Undo(key, this);

        public bool RedoSnapshot(string key)
            => MementoManager.Instance.Redo(key, this);
    }
}
