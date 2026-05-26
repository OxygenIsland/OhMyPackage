// ============================================================
//  MementoManager — 全局单例管理器
//
//  设计来源调研：
//    · UnityCommunity/UnitySingleton — PersistentMonoSingleton 跨场景
//    · Tarodev Unity Singleton — StaticInstance + DontDestroyOnLoad
//    · codeproject RoundStack — 容量控制
//    · postsharp.net IMementoable — 接口分层
//
//  核心特性：
//    1. 全局单例，DontDestroyOnLoad，任意脚本 MementoManager.Instance.xxx
//    2. 多 Key 隔离：不同系统/对象各自独立的 HistoryChannel
//    3. 泛型 API：Save<TState> / Undo / Redo / JumpTo
//    4. 事件回调：OnSaved / OnUndone / OnRedone / OnCleared
//    5. 容量限制：每个 Channel 最多保留 N 条历史
//    6. 持久化接口：ExportJson / ImportJson 方便存档
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OhMyPackage.MementoManager
{
    // ── 事件参数 ──────────────────────────────────────────────
    public readonly struct MementoEventArgs
    {
        public readonly string   Key;
        public readonly IMemento Memento;
        public readonly string   Action;   // "Save" / "Undo" / "Redo" / "Jump" / "Clear"

        public MementoEventArgs(string key, IMemento memento, string action)
        {
            Key     = key;
            Memento = memento;
            Action  = action;
        }
    }

    // ── MementoManager ────────────────────────────────────────
    [DefaultExecutionOrder(-500)]                    // 比大多数 Manager 先初始化
    public sealed class MementoManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────
        public static MementoManager Instance { get; private set; }

        [Header("全局默认配置")]
        [Tooltip("每个 Channel 默认最多保留多少条历史（可被单次调用覆盖）")]
        [SerializeField] private int _defaultMaxCapacity = 50;

        // ── 事件 ──────────────────────────────────────────────
        /// <summary>每次 Save / Undo / Redo / Jump / Clear 后触发</summary>
        public event Action<MementoEventArgs> OnMementoChanged;

        // ── 内部存储 ──────────────────────────────────────────
        // Key → HistoryChannel
        private readonly Dictionary<string, HistoryChannel> _channels
            = new Dictionary<string, HistoryChannel>();

        // Key → 版本计数器
        private readonly Dictionary<string, int> _versionCounters
            = new Dictionary<string, int>();

        // ══════════════════════════════════════════════════════
        //  Lifecycle
        // ══════════════════════════════════════════════════════
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── 懒加载自动创建（不挂 GameObject 也能用）──────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("[MementoManager]");
            go.AddComponent<MementoManager>();
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API — Save
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 保存 IOriginator 的当前状态快照。
        /// 推荐用法：MementoManager.Instance.Save("MapEditor", this, "修改地形");
        /// </summary>
        public IMemento Save(string key, IOriginator originator, string label = "")
        {
            var memento = originator.SaveToMemento();
            PushInternal(key, memento, label);
            return memento;
        }

        /// <summary>
        /// 泛型版本：直接传入状态数据，无需实现 IOriginator。
        /// 内部会做深拷贝，保证快照独立。
        /// </summary>
        /// <example>
        /// var state = new MapState { tileData = ... };
        /// MementoManager.Instance.Save("Map", state, "初始地图");
        /// </example>
        public IMemento Save<TState>(string key, TState state, string label = "")
            where TState : class, new()
        {
            var copy    = StateSerializer.DeepCopy(state);
            int version = NextVersion(key);
            var memento = new Memento<TState>(key, label, copy, version);

            GetOrCreateChannel(key).Push(memento);
            Notify(key, memento, "Save");
            return memento;
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API — Load / Peek
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 读取当前快照里的 State 数据（不改变历史指针）。
        /// </summary>
        public bool TryPeekState<TState>(string key, out TState state)
            where TState : class, new()
        {
            state = default;
            if (!_channels.TryGetValue(key, out var ch)) return false;
            var current = ch.Current();
            if (current is Memento<TState> typed)
            {
                state = StateSerializer.DeepCopy(typed.State);  // 返回副本
                return true;
            }
            return false;
        }

        /// <summary>
        /// 读取当前 IMemento 元数据（不拆包 State）。
        /// </summary>
        public IMemento PeekCurrent(string key)
        {
            return _channels.TryGetValue(key, out var ch) ? ch.Current() : null;
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API — Undo / Redo
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 将指定 Key 的历史回退一步，并把状态应用到 originator。
        /// </summary>
        public bool Undo(string key, IOriginator originator)
        {
            if (!TryUndo(key, out var memento)) return false;
            originator.RestoreFromMemento(memento);
            return true;
        }

        /// <summary>
        /// 泛型版本：回退并直接返回 TState（适合工具链不持有 IOriginator 引用时）。
        /// </summary>
        public bool Undo<TState>(string key, out TState state)
            where TState : class, new()
        {
            state = default;
            if (!TryUndo(key, out var memento)) return false;
            if (memento is Memento<TState> typed)
            {
                state = StateSerializer.DeepCopy(typed.State);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 重做：前进一步并应用到 originator。
        /// </summary>
        public bool Redo(string key, IOriginator originator)
        {
            if (!TryRedo(key, out var memento)) return false;
            originator.RestoreFromMemento(memento);
            return true;
        }

        public bool Redo<TState>(string key, out TState state)
            where TState : class, new()
        {
            state = default;
            if (!TryRedo(key, out var memento)) return false;
            if (memento is Memento<TState> typed)
            {
                state = StateSerializer.DeepCopy(typed.State);
                return true;
            }
            return false;
        }

        // ── 可用性查询 ────────────────────────────────────────
        public bool CanUndo(string key) =>
            _channels.TryGetValue(key, out var ch) && ch.CanUndo;

        public bool CanRedo(string key) =>
            _channels.TryGetValue(key, out var ch) && ch.CanRedo;

        // ══════════════════════════════════════════════════════
        //  PUBLIC API — JumpTo / History
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 直接跳转到指定版本号的快照（存档点跳转场景）。
        /// </summary>
        public bool JumpToVersion<TState>(string key, int version, out TState state)
            where TState : class, new()
        {
            state = default;
            if (!_channels.TryGetValue(key, out var ch)) return false;
            var memento = ch.JumpToVersion(version);
            if (memento is Memento<TState> typed)
            {
                state = StateSerializer.DeepCopy(typed.State);
                Notify(key, memento, "Jump");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取某个 Key 的完整历史列表（只读，用于 UI 展示）。
        /// </summary>
        public IReadOnlyList<IMemento> GetHistory(string key)
        {
            if (_channels.TryGetValue(key, out var ch))
                return ch.GetHistory();
            return Array.Empty<IMemento>();
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API — 持久化
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 把当前快照序列化为 JSON（用于写入磁盘/PlayerPrefs）。
        /// 只导出当前指针位置的 State，不导出整条历史。
        /// </summary>
        public string ExportCurrentStateJson<TState>(string key)
            where TState : class, new()
        {
            if (!TryPeekState<TState>(key, out var state)) return null;
            return StateSerializer.Serialize(state);
        }

        /// <summary>
        /// 从 JSON 字符串导入并作为一条新快照推入历史。
        /// </summary>
        public IMemento ImportStateFromJson<TState>(string key, string json, string label = "Imported")
            where TState : class, new()
        {
            var state = StateSerializer.Deserialize<TState>(json);
            return Save(key, state, label);
        }

        // ══════════════════════════════════════════════════════
        //  PUBLIC API — 管理
        // ══════════════════════════════════════════════════════

        /// <summary>清空某个 Key 的全部历史</summary>
        public void ClearHistory(string key)
        {
            if (_channels.TryGetValue(key, out var ch))
            {
                ch.Clear();
                Notify(key, null, "Clear");
            }
        }

        /// <summary>清空所有 Key 的历史</summary>
        public void ClearAll()
        {
            foreach (var kv in _channels)
                kv.Value.Clear();
            _channels.Clear();
            _versionCounters.Clear();
        }

        /// <summary>查询某个 Key 是否存在历史</summary>
        public bool HasHistory(string key) =>
            _channels.TryGetValue(key, out var ch) && ch.Count > 0;

        /// <summary>
        /// 动态修改某个 Key 的容量上限。
        /// </summary>
        public void SetMaxCapacity(string key, int capacity)
        {
            GetOrCreateChannel(key, capacity);
        }

        // ══════════════════════════════════════════════════════
        //  内部辅助
        // ══════════════════════════════════════════════════════
        private HistoryChannel GetOrCreateChannel(string key, int capacity = -1)
        {
            if (!_channels.TryGetValue(key, out var ch))
            {
                int cap = capacity > 0 ? capacity : _defaultMaxCapacity;
                ch = new HistoryChannel(key, cap);
                _channels[key] = ch;
            }
            return ch;
        }

        private int NextVersion(string key)
        {
            _versionCounters.TryGetValue(key, out int v);
            _versionCounters[key] = v + 1;
            return v + 1;
        }

        private void PushInternal(string key, IMemento memento, string label)
        {
            GetOrCreateChannel(key).Push(memento);
            Notify(key, memento, "Save");
        }

        private bool TryUndo(string key, out IMemento memento)
        {
            memento = null;
            if (!_channels.TryGetValue(key, out var ch)) return false;
            memento = ch.Undo();
            if (memento == null) return false;
            Notify(key, memento, "Undo");
            return true;
        }

        private bool TryRedo(string key, out IMemento memento)
        {
            memento = null;
            if (!_channels.TryGetValue(key, out var ch)) return false;
            memento = ch.Redo();
            if (memento == null) return false;
            Notify(key, memento, "Redo");
            return true;
        }

        private void Notify(string key, IMemento memento, string action) =>
            OnMementoChanged?.Invoke(new MementoEventArgs(key, memento, action));
    }
}
