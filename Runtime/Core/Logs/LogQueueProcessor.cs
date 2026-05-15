using UnityEngine;

/// <summary>
/// 日志队列处理器 - 在后台自动处理来自子线程的日志
/// 由 NLogManager 自动创建和管理
/// </summary>
public sealed class LogQueueProcessor : MonoBehaviour
{
    private static LogQueueProcessor _instance;

    public static void Create()
    {
        if (_instance != null) return;

        var go = new GameObject("[NLog] Queue Processor");
        go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        _instance = go.AddComponent<LogQueueProcessor>();
        DontDestroyOnLoad(go);
    }

    public static void Destroy()
    {
        if (_instance != null)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    private void Update()
    {
        UnityConsoleTarget.ProcessQueuedLogs();
    }
}
