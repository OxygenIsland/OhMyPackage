using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NLog;
using NLog.Targets;
using UnityEngine;

[Target("UnityConsole")]
public class UnityConsoleTarget : TargetWithLayout
{
    // 通过 NLog.config 配置，只对哪些级别采集调用栈
    // 默认只采集 Error 和 Fatal，性能优先
    public string StackTraceLevels { get; set; } = "Error,Fatal";

    private HashSet<string> _stackTraceLevelSet;

    private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;
    private static readonly Queue<(LogLevel level, string message)> LogQueue = new Queue<(LogLevel, string)>();
    private static readonly object LockObject = new object();

    private HashSet<string> StackTraceLevelSet
    {
        get
        {
            if (_stackTraceLevelSet == null)
            {
                _stackTraceLevelSet = new HashSet<string>(
                    StackTraceLevels.Split(','),
                    System.StringComparer.OrdinalIgnoreCase
                );
            }
            return _stackTraceLevelSet;
        }
    }

    protected override void Write(LogEventInfo logEvent)
    {
        string message = this.Layout.Render(logEvent);

        // 检查是否在主线程
        if (Thread.CurrentThread.ManagedThreadId == MainThreadId)
        {
            // 主线程：直接输出
            OutputLog(logEvent.Level, message);
        }
        else
        {
            // 子线程：加入队列，等待主线程处理
            lock (LockObject)
            {
                LogQueue.Enqueue((logEvent.Level, message));
            }
        }
    }

    /// <summary>
    /// 处理队列中的日志（应在主线程中调用，例如在NLogManager中）
    /// </summary>
    public static void ProcessQueuedLogs()
    {
        lock (LockObject)
        {
            while (LogQueue.Count > 0)
            {
                var (level, message) = LogQueue.Dequeue();
                OutputLog(level, message);
            }
        }
    }

    private static void OutputLog(LogLevel level, string message)
    {
        switch (level.Name.ToLower())
        {
            case "fatal":
            case "error":
                UnityEngine.Debug.LogError(message);
                break;
            case "warn":
                UnityEngine.Debug.LogWarning(message);
                break;
            default:
                UnityEngine.Debug.Log(message);
                break;
        }
    }
}