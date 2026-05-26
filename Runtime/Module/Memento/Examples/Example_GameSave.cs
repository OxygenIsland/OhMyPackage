// ============================================================
//  示例 2 — 游戏存档 & 检查点
//  场景：通关检查点 / 死亡回档，不继承基类，直接用泛型 API。
//  展示"多 Key 并行"——Player 和 World 各自独立的历史通道。
// ============================================================
using System;
using UnityEngine;

namespace OhMyPackage.MementoManager.Examples
{
    // ── State DTO ─────────────────────────────────────────────
    [Serializable]
    public class PlayerState
    {
        public float   hp;
        public float   mp;
        public Vector3 position;
        public int     gold;
        public int     level;
    }

    [Serializable]
    public class WorldState
    {
        public string sceneName;
        public int    defeatedEnemyCount;
        public bool[] doorOpened;         // 门的开关状态
    }

    // ── 游戏存档管理器 ────────────────────────────────────────
    public class GameSaveSystem : MonoBehaviour
    {
        // Key 常量——集中管理避免字符串散落各处
        public const string KEY_PLAYER    = "Player";
        public const string KEY_WORLD     = "World";
        public const string KEY_CHECKPOINT = "Checkpoint";

        [Header("引用")]
        [SerializeField] private Transform _playerTransform;

        // ── 模拟数据 ──────────────────────────────────────────
        private PlayerState _player = new PlayerState { hp = 100, mp = 50, level = 1 };
        private WorldState  _world  = new WorldState  { sceneName = "Forest", doorOpened = new bool[10] };

        private void Start()
        {
            // 订阅 Manager 事件，记录日志
            MementoManager.Instance.OnMementoChanged += OnChanged;

            // 游戏开始时保存初始快照
            CreateCheckpoint("游戏开始");
        }

        private void OnDestroy()
        {
            if (MementoManager.Instance != null)
                MementoManager.Instance.OnMementoChanged -= OnChanged;
        }

        // ── 检查点存档 ────────────────────────────────────────
        /// <summary>到达检查点时同时保存 Player 和 World 的状态</summary>
        public void CreateCheckpoint(string label)
        {
            if (_playerTransform != null)
                _player.position = _playerTransform.position;

            MementoManager.Instance.Save(KEY_PLAYER, _player,  $"[CP] {label}");
            MementoManager.Instance.Save(KEY_WORLD,  _world,   $"[CP] {label}");

            Debug.Log($"[SaveSystem] 检查点已创建：{label}");
        }

        // ── 死亡回档 ──────────────────────────────────────────
        /// <summary>死亡后回到上一个检查点</summary>
        public void RespawnFromCheckpoint()
        {
            if (MementoManager.Instance.Undo<PlayerState>(KEY_PLAYER, out var pState))
            {
                _player = pState;
                if (_playerTransform != null)
                    _playerTransform.position = pState.position;
            }

            if (MementoManager.Instance.Undo<WorldState>(KEY_WORLD, out var wState))
                _world = wState;

            Debug.Log($"[SaveSystem] 已回档至: HP={_player.hp}, 场景={_world.sceneName}");
        }

        // ── 多存档槽（JumpTo）────────────────────────────────
        /// <summary>
        /// 跳转到指定存档版本（存档槽选择界面使用）。
        /// </summary>
        public void LoadSaveSlot(int version)
        {
            if (MementoManager.Instance.JumpToVersion<PlayerState>(KEY_PLAYER, version, out var s))
            {
                _player = s;
                Debug.Log($"[SaveSystem] 加载存档槽 v{version}: Lv{s.level} HP={s.hp}");
            }
        }

        // ── 持久化（写磁盘）──────────────────────────────────
        public void SaveToPlayerPrefs()
        {
            var json = MementoManager.Instance.ExportCurrentStateJson<PlayerState>(KEY_PLAYER);
            if (json != null)
            {
                PlayerPrefs.SetString("SavedPlayer", json);
                PlayerPrefs.Save();
                Debug.Log("[SaveSystem] 已写入 PlayerPrefs");
            }
        }

        public void LoadFromPlayerPrefs()
        {
            if (!PlayerPrefs.HasKey("SavedPlayer")) return;
            var json = PlayerPrefs.GetString("SavedPlayer");
            MementoManager.Instance.ImportStateFromJson<PlayerState>(KEY_PLAYER, json, "从磁盘加载");
            Debug.Log("[SaveSystem] 已从 PlayerPrefs 读取");
        }

        // ── 历史快照查询（存档列表 UI）───────────────────────
        public void PrintHistory()
        {
            var history = MementoManager.Instance.GetHistory(KEY_PLAYER);
            Debug.Log($"[SaveSystem] Player 历史共 {history.Count} 条:");
            foreach (var m in history)
                Debug.Log($"  {m}");
        }

        private void OnChanged(MementoEventArgs e)
            => Debug.Log($"[MementoEvent] {e.Action} | Key={e.Key} | {e.Memento?.Label}");
    }
}
