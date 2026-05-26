// ============================================================
//  MementoManager — Editor 调试窗口
//  菜单：Tools > MementoManager > 历史快照查看器
//  实时展示所有 Key 及其历史，支持一键 Undo/Redo
// ============================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OhMyPackage.MementoManager.Editor
{
    public class MementoManagerDebugWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool    _autoRefresh = true;
        private double  _lastRefresh;

        [MenuItem("Tools/MementoManager/历史快照查看器")]
        public static void ShowWindow()
            => GetWindow<MementoManagerDebugWindow>("Memento 快照查看器");

        private void OnEnable()  => EditorApplication.update += OnEditorUpdate;
        private void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        private void OnEditorUpdate()
        {
            if (_autoRefresh && EditorApplication.timeSinceStartup - _lastRefresh > 0.5f)
            {
                _lastRefresh = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            // ── 工具栏 ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "自动刷新", EditorStyles.toolbarButton, GUILayout.Width(80));
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(50))) Repaint();
            if (GUILayout.Button("清空全部", EditorStyles.toolbarButton, GUILayout.Width(70)))
                MementoManager.Instance?.ClearAll();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (MementoManager.Instance == null)
            {
                EditorGUILayout.HelpBox("MementoManager 未运行（需在 PlayMode）", MessageType.Info);
                return;
            }

            // ── 读取内部 _channels（反射，仅用于调试）─────────
            var channelsField = typeof(MementoManager).GetField(
                "_channels", BindingFlags.NonPublic | BindingFlags.Instance);
            if (channelsField == null) { EditorGUILayout.HelpBox("无法读取内部字段", MessageType.Warning); return; }

            var channels = channelsField.GetValue(MementoManager.Instance)
                           as Dictionary<string, HistoryChannel>;
            if (channels == null || channels.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无历史快照", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var kv in channels)
            {
                string key = kv.Key;
                var    ch  = kv.Value;

                // ── Key 标题 ──────────────────────────────────
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"🗂 {key}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = Color.gray } };
                EditorGUILayout.LabelField(
                    $"共 {ch.Count} 条  当前 [{ch.CurrentIndex}]",
                    labelStyle, GUILayout.Width(140));

                GUI.enabled = ch.CanUndo;
                if (GUILayout.Button("↩", GUILayout.Width(28)))
                    MementoManager.Instance.CanUndo(key); // 无 Originator 时只移动指针

                GUI.enabled = ch.CanRedo;
                if (GUILayout.Button("↪", GUILayout.Width(28)))
                    MementoManager.Instance.CanRedo(key);

                GUI.enabled = true;
                if (GUILayout.Button("✕", GUILayout.Width(28)))
                    MementoManager.Instance.ClearHistory(key);

                EditorGUILayout.EndHorizontal();

                // ── 快照列表 ──────────────────────────────────
                var history = ch.GetHistory();
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    var m       = history[i];
                    bool isCurrent = (i == ch.CurrentIndex);

                    var bg = new GUIStyle(EditorStyles.label)
                    {
                        normal = { textColor = isCurrent
                            ? new Color(0.3f, 0.8f, 0.4f)
                            : new Color(0.75f, 0.75f, 0.75f) }
                    };

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(isCurrent ? "▶" : "  ", GUILayout.Width(16));
                    EditorGUILayout.LabelField($"v{m.Version}", GUILayout.Width(40));
                    EditorGUILayout.LabelField(m.Label.Length > 0 ? m.Label : "(无标签)", bg);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(m.CreatedAt.ToString("HH:mm:ss"),
                        EditorStyles.miniLabel, GUILayout.Width(65));
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
