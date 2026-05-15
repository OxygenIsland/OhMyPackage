// Assets/Scripts/Logging/NLogManager.cs
using System.IO;
using UnityEngine;
using NLog;
using NLog.Config;

public static class NLogManager
{
    private static bool _initialized = false;
    public static readonly string logDirectory = Path.Combine(Application.persistentDataPath, "Logs");
    public static void Initialize()
    {
        if (_initialized) return;

        // 从 StreamingAssets 加载配置
        string configPath = Path.Combine(Application.streamingAssetsPath, "NLog.config");

        if (File.Exists(configPath))
        {
            // 注入 Unity 平台路径变量，供 NLog.config 中 ${var:logDir} 使用
            // 必须在 XmlLoggingConfiguration 解析前设置，否则变量在解析时不可用
            NLog.GlobalDiagnosticsContext.Set("logDir", logDirectory);

            var config = new XmlLoggingConfiguration(configPath);
            
            // 非编辑器模式下，移除 UnityConsole 输出规则
#if !UNITY_EDITOR
            foreach (var rule in config.LoggingRules)
            {
                var targetsToRemove = new System.Collections.Generic.List<NLog.Targets.Target>();
                foreach (var target in rule.Targets)
                {
                    if (target.Name == "unityConsole")
                        targetsToRemove.Add(target);
                }
                foreach (var target in targetsToRemove)
                {
                    rule.Targets.Remove(target);
                }
            }
#endif
            
            LogManager.Configuration = config;
        }
        else
        {
            // 代码方式兜底配置
            SetupFallbackConfig();
        }

        // 创建日志队列处理器（自动处理子线程日志）
        LogQueueProcessor.Create();

        _initialized = true;
        Debug.Log("[NLogManager] NLog initialized.");
    }

    private static void SetupFallbackConfig()
    {
        var config = new LoggingConfiguration();

        Directory.CreateDirectory(logDirectory);
        string logFile = Path.Combine(logDirectory, "game.log");

        var fileTarget = new NLog.Targets.FileTarget("logfile")
        {
            FileName = logFile,
            Layout = "${time} [${level:uppercase=true}] ${logger} - ${message}${when:when='${level:uppercase}' == 'ERROR' OR '${level:uppercase}' == 'FATAL':inner=${newline}${stacktrace:format=detailedFlat:topFrames=50:separator=\n}}${onexception:inner=${newline}${exception:format=tostring}}",
            MaxArchiveFiles = 5,
            ArchiveEvery = NLog.Targets.FileArchivePeriod.Day
        };

        config.AddTarget(fileTarget);
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);

        // 仅在编辑器模式下输出到 Unity Console
#if UNITY_EDITOR
        var unityConsoleTarget = new UnityConsoleTarget
        {
            Name = "unityConsole",
            Layout = "${time} [${level:uppercase=true}] ${logger} - ${message}"
        };
        config.AddTarget(unityConsoleTarget);
        config.AddRule(LogLevel.Debug, LogLevel.Fatal, unityConsoleTarget);
#endif

        LogManager.Configuration = config;
    }

    public static void Shutdown()
    {
        // 销毁日志队列处理器
        LogQueueProcessor.Destroy();

        LogManager.Shutdown();
    }
}