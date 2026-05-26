// ============================================================
//  示例 1 — 关卡编辑器（Level Editor）
//  场景：在自定义 EditorWindow 或运行时关卡编辑器里，
//        每次操作前保存快照，支持 Ctrl+Z / Ctrl+Y。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OhMyPackage.MementoManager.Examples
{
    // ── State DTO（必须标注 [Serializable]）──────────────────
    [Serializable]
    public class MapEditorState
    {
        public int   width;
        public int   height;
        public int[] tiles;           // 平铺 tile 类型数组
        public int   selectedTileId;

        // 深拷贝友好：全是值类型 / 基础类型
        public MapEditorState Clone() => new MapEditorState
        {
            width          = width,
            height         = height,
            tiles          = tiles != null ? (int[])tiles.Clone() : null,
            selectedTileId = selectedTileId,
        };
    }

    // ── MapEditor：继承 MementoOriginator 基类 ───────────────
    public class MapEditor : MementoOriginator<MapEditorState>
    {
        private const string KEY = "MapEditor";

        [Header("运行时地图数据")]
        public int   mapWidth  = 10;
        public int   mapHeight = 10;

        private int[]  _tiles;
        private int    _selectedTileId;

        // ── Unity 生命周期 ────────────────────────────────────
        private void Start()
        {
            _tiles = new int[mapWidth * mapHeight];
            // 初始状态也保存一份，作为 Undo 基线
            SaveSnapshot(KEY, "初始地图");
        }

        private void Update()
        {
            // Ctrl+Z
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftCommand))
                && Input.GetKeyDown(KeyCode.Z))
            {
                if (UndoSnapshot(KEY))
                    Debug.Log($"[MapEditor] Undo → 当前: {MementoManager.Instance.PeekCurrent(KEY)?.Label}");
                else
                    Debug.Log("[MapEditor] 已到最早历史");
            }

            // Ctrl+Y
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftCommand))
                && Input.GetKeyDown(KeyCode.Y))
            {
                if (RedoSnapshot(KEY))
                    Debug.Log($"[MapEditor] Redo → 当前: {MementoManager.Instance.PeekCurrent(KEY)?.Label}");
                else
                    Debug.Log("[MapEditor] 已到最新历史");
            }
        }

        // ── IOriginator<MapEditorState> ───────────────────────
        public override MapEditorState CaptureState() => new MapEditorState
        {
            width          = mapWidth,
            height         = mapHeight,
            tiles          = (int[])(_tiles?.Clone() ?? Array.Empty<int>()),
            selectedTileId = _selectedTileId,
        };

        public override void ApplyState(MapEditorState s)
        {
            mapWidth       = s.width;
            mapHeight      = s.height;
            _tiles         = (int[])(s.tiles?.Clone() ?? Array.Empty<int>());
            _selectedTileId = s.selectedTileId;
            Debug.Log($"[MapEditor] 恢复地图 {mapWidth}×{mapHeight}");
        }

        // ── 业务方法（每次修改前先保存快照）──────────────────
        public void PaintTile(int x, int y, int tileId)
        {
            SaveSnapshot(KEY, $"绘制 ({x},{y}) = {tileId}");
            _tiles[y * mapWidth + x] = tileId;
        }

        public void FloodFill(int startX, int startY, int tileId)
        {
            SaveSnapshot(KEY, $"填充 ({startX},{startY}) = {tileId}");
            // 填充逻辑省略...
        }

        /// <summary>
        /// 批量操作：先保存一次快照，批量修改视为单个 Undo 步骤。
        /// </summary>
        public void BatchOperation(Action batchAction, string label)
        {
            SaveSnapshot(KEY, label);
            batchAction?.Invoke();
        }

        // ── 历史列表（用于 UI 显示）──────────────────────────
        public IReadOnlyList<IMemento> GetUndoHistory()
            => MementoManager.Instance.GetHistory(KEY);
    }
}
