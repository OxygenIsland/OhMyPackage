// ============================================================
//  示例 3 — 自定义 EditorWindow 工具（节点图编辑器）
//  场景：节点连线编辑，纯 C# 类，使用 MementoOriginatorBase。
//  展示：批量操作合并为单个 Undo 步骤（CompoundOperation）。
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OhMyPackage.MementoManager.Examples
{
    // ── State DTO ─────────────────────────────────────────────
    [Serializable]
    public class NodeGraphState
    {
        public List<NodeData>   nodes;
        public List<EdgeData>   edges;
        public Vector2          viewOffset;
        public float            zoom;
    }

    [Serializable]
    public class NodeData
    {
        public string id;
        public string title;
        public float  x, y, w, h;
    }

    [Serializable]
    public class EdgeData
    {
        public string fromId;
        public string toId;
    }

    // ── NodeGraphEditor：纯 C# 类版本 ────────────────────────
    public class NodeGraphEditor : MementoOriginatorBase<NodeGraphState>
    {
        private const string KEY = "NodeGraph";

        private NodeGraphState _state = new NodeGraphState
        {
            nodes      = new List<NodeData>(),
            edges      = new List<EdgeData>(),
            viewOffset = Vector2.zero,
            zoom       = 1f,
        };

        // ── IOriginator<NodeGraphState> ───────────────────────
        public override NodeGraphState CaptureState()
        {
            // 返回结构完整的深拷贝（JsonUtility 在 Save 内部也会再拷贝一次，双重保险）
            return new NodeGraphState
            {
                nodes      = new List<NodeData>(_state.nodes),
                edges      = new List<EdgeData>(_state.edges),
                viewOffset = _state.viewOffset,
                zoom       = _state.zoom,
            };
        }

        public override void ApplyState(NodeGraphState s)
        {
            _state.nodes      = new List<NodeData>(s.nodes);
            _state.edges      = new List<EdgeData>(s.edges);
            _state.viewOffset = s.viewOffset;
            _state.zoom       = s.zoom;
            OnGraphChanged?.Invoke();
        }

        // ── 回调（EditorWindow 订阅后刷新 UI）────────────────
        public event Action OnGraphChanged;

        // ── 业务方法 ──────────────────────────────────────────
        public void AddNode(string id, string title, float x, float y)
        {
            SaveSnapshot(KEY, $"添加节点 [{title}]");
            _state.nodes.Add(new NodeData { id = id, title = title, x = x, y = y, w = 120, h = 60 });
            OnGraphChanged?.Invoke();
        }

        public void DeleteNode(string id)
        {
            SaveSnapshot(KEY, $"删除节点 [{id}]");
            _state.nodes.RemoveAll(n => n.id == id);
            _state.edges.RemoveAll(e => e.fromId == id || e.toId == id);
            OnGraphChanged?.Invoke();
        }

        public void ConnectNodes(string fromId, string toId)
        {
            SaveSnapshot(KEY, $"连接 {fromId} → {toId}");
            _state.edges.Add(new EdgeData { fromId = fromId, toId = toId });
            OnGraphChanged?.Invoke();
        }

        // ── 批量操作：保存一次快照，多步视为一个 Undo 单元 ───
        public void BeginBatch(string label) => SaveSnapshot(KEY, $"[批量开始] {label}");
        // 批量操作完成后不需要再 Save，因为 Undo 会直接还原到 BeginBatch 之前

        public bool Undo() => UndoSnapshot(KEY);
        public bool Redo() => RedoSnapshot(KEY);
        public bool CanUndo => MementoManager.Instance?.CanUndo(KEY) ?? false;
        public bool CanRedo => MementoManager.Instance?.CanRedo(KEY) ?? false;

        public IReadOnlyList<IMemento> History
            => MementoManager.Instance?.GetHistory(KEY) ?? Array.Empty<IMemento>();
    }

    // ── EditorWindow 集成示例 ─────────────────────────────────
#if UNITY_EDITOR
    public class NodeGraphEditorWindow : EditorWindow
    {
        private NodeGraphEditor _graph;

        [MenuItem("Tools/MementoManager/节点图编辑器示例")]
        public static void ShowWindow()
            => GetWindow<NodeGraphEditorWindow>("节点图编辑器");

        private void OnEnable()
        {
            _graph = new NodeGraphEditor();
            _graph.OnGraphChanged += Repaint;
        }

        private void OnDisable()
        {
            if (_graph != null) _graph.OnGraphChanged -= Repaint;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.enabled = _graph.CanUndo;
            if (GUILayout.Button("↩ Undo", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                _graph.Undo();
                Repaint();
            }
            GUI.enabled = _graph.CanRedo;
            if (GUILayout.Button("↪ Redo", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                _graph.Redo();
                Repaint();
            }
            GUI.enabled = true;

            if (GUILayout.Button("+ 添加节点", EditorStyles.toolbarButton))
                _graph.AddNode(Guid.NewGuid().ToString()[..6], "New Node",
                               UnityEngine.Random.Range(50, 400),
                               UnityEngine.Random.Range(50, 300));

            EditorGUILayout.EndHorizontal();

            // 历史列表
            EditorGUILayout.LabelField("历史快照", EditorStyles.boldLabel);
            foreach (var m in _graph.History)
                EditorGUILayout.LabelField($"  v{m.Version}  {m.Label}  @ {m.CreatedAt:HH:mm:ss}");
        }
    }
#endif
}
