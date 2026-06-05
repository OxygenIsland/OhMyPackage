using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MyGame.Toolkit.Network;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace OhMyPackage
{
    public sealed partial class Debugger
    {
        /// <summary>
        /// Bug 反馈窗口：测试同事可填写问题描述、选择严重程度，一键打包日志并上传到服务器。
        /// </summary>
        private sealed class FeedbackWindow : IDebuggerWindow
        {
            private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

            // ── 状态枚举 ─────────────────────────────────────────────────
            private enum UploadState { Idle, Uploading, Success, Failed }

            // ── 严重程度选项 ──────────────────────────────────────────────
            private static readonly string[] SeverityLabels = { "普通", "重要", "严重", "崩溃" };
            private static readonly string[] SeverityKeys   = { "normal", "major", "critical", "crash" };

            // ── 字段 ─────────────────────────────────────────────────────
            private IWebRequestService _webRequestService;
            private string _description  = string.Empty;
            private int    _severityIndex = 0;
            private string _uploadUrl    = string.Empty;
            private UploadState _state   = UploadState.Idle;
            private string _statusMessage = string.Empty;
            private CancellationTokenSource _cts;
            private Vector2 _scrollPos = Vector2.zero;

            // ── IDebuggerWindow 生命周期 ──────────────────────────────────

            /// <summary>
            /// 初始化。args[0]: IWebRequestService, args[1]: 默认上传 URL（可选）。
            /// </summary>
            public void Initialize(params object[] args)
            {
                if (args != null && args.Length > 0 && args[0] is IWebRequestService svc)
                    _webRequestService = svc;

                if (args != null && args.Length > 1 && args[1] is string url)
                    _uploadUrl = url ?? string.Empty;
            }

            public void Shutdown()
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }

            public void OnEnter()  { }
            public void OnLeave()  { }
            public void OnUpdate() { }

            // ── IMGUI 绘制 ────────────────────────────────────────────────

            public void OnDraw()
            {
                _scrollPos = GUILayout.BeginScrollView(_scrollPos);
                {
                    GUILayout.Label("<b>Bug 反馈 / Feedback</b>");

                    // ── 问题描述 ──────────────────────────────────────────
                    GUILayout.BeginVertical("box");
                    {
                        GUILayout.Label("问题描述（Bug Description）:");
                        _description = GUILayout.TextArea(_description, 300, GUILayout.Height(80f));
                    }
                    GUILayout.EndVertical();

                    // ── 严重程度 ──────────────────────────────────────────
                    GUILayout.BeginVertical("box");
                    {
                        GUILayout.Label("严重程度（Severity）:");
                        GUILayout.BeginHorizontal();
                        {
                            for (int i = 0; i < SeverityLabels.Length; i++)
                            {
                                bool isSelected = (_severityIndex == i);
                                if (GUILayout.Toggle(isSelected, SeverityLabels[i], "button", GUILayout.Width(80f)) && !isSelected)
                                    _severityIndex = i;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();

                    // ── 上传地址 ──────────────────────────────────────────
                    GUILayout.BeginVertical("box");
                    {
                        GUILayout.Label("上传地址（Upload URL）:");
                        _uploadUrl = GUILayout.TextField(_uploadUrl, 512);
                    }
                    GUILayout.EndVertical();

                    // ── 操作按钮 ──────────────────────────────────────────
                    bool canUpload = _state != UploadState.Uploading
                                     && !string.IsNullOrEmpty(_uploadUrl)
                                     && _webRequestService != null;

                    GUILayout.BeginHorizontal();
                    {
                        GUI.enabled = canUpload;
                        string btnLabel = (_state == UploadState.Uploading) ? "上传中..." : "一键上传 Log";
                        if (GUILayout.Button(btnLabel, GUILayout.Height(40f)))
                            UploadFeedbackAsync().Forget();
                        GUI.enabled = true;

                        if (_state == UploadState.Uploading)
                        {
                            if (GUILayout.Button("取消", GUILayout.Width(60f), GUILayout.Height(40f)))
                                _cts?.Cancel();
                        }
                    }
                    GUILayout.EndHorizontal();

                    // ── 状态消息 ──────────────────────────────────────────
                    if (!string.IsNullOrEmpty(_statusMessage))
                    {
                        Color prevColor = GUI.color;
                        GUI.color = _state switch
                        {
                            UploadState.Success => Color.green,
                            UploadState.Failed  => Color.red,
                            _                   => Color.white,
                        };
                        GUILayout.Label(_statusMessage);
                        GUI.color = prevColor;
                    }

                    // ── 环境摘要（便于测试同事核对设备信息）────────────────
                    GUILayout.BeginVertical("box");
                    {
                        GUILayout.Label("<b>环境摘要</b>");
                        GUILayout.Label($"版本: {Application.version}  平台: {Application.platform}");
                        GUILayout.Label($"设备: {SystemInfo.deviceModel}");
                        GUILayout.Label($"系统: {SystemInfo.operatingSystem}");
                        GUILayout.Label($"当前场景: {SceneManager.GetActiveScene().name}");
                        GUILayout.Label($"报告时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    }
                    GUILayout.EndVertical();
                }
                GUILayout.EndScrollView();
            }

            // ── 上传逻辑 ──────────────────────────────────────────────────

            private async UniTaskVoid UploadFeedbackAsync()
            {
                _state = UploadState.Uploading;
                _statusMessage = "正在打包日志...";

                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                CancellationToken token = _cts.Token;

                // 1. 在线程池打包日志 ZIP，避免阻塞主线程
                byte[] zipBytes;
                try
                {
                    zipBytes = await UniTask.RunOnThreadPool(PackLogsToZip, cancellationToken: token);
                }
                catch (OperationCanceledException)
                {
                    _state = UploadState.Idle;
                    _statusMessage = "已取消。";
                    return;
                }
                catch (Exception ex)
                {
                    _state = UploadState.Failed;
                    _statusMessage = $"打包失败：{ex.Message}";
                    Log.Error($"FeedbackWindow: pack error — {ex}");
                    return;
                }

                _statusMessage = "正在上传到服务器...";

                // 2. 构建附带描述和严重程度的元数据 JSON
                string metaJson = BuildFeedbackMeta(_description, SeverityKeys[_severityIndex]);
                string zipName  = $"feedback_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

                // 3. Multipart POST 上传
                var formSections = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("meta", metaJson, "application/json"),
                    new MultipartFormFileSection("file", zipBytes, zipName, "application/zip"),
                };

                var options = new WebRequestOptions { Tag = "feedback", Timeout = 30 };

                WebRequestResult<FeedbackServerResponse> result;
                try
                {
                    result = await _webRequestService.PostMultipartAsync<FeedbackServerResponse>(
                        _uploadUrl, formSections, options, token);
                }
                catch (OperationCanceledException)
                {
                    _state = UploadState.Idle;
                    _statusMessage = "已取消。";
                    return;
                }

                if (result.Success)
                {
                    _state = UploadState.Success;
                    string ticketId = result.Data?.TicketId;
                    _statusMessage = string.IsNullOrEmpty(ticketId)
                        ? "上传成功！"
                        : $"上传成功！工单号：{ticketId}";
                    Log.Info($"FeedbackWindow: upload succeeded. TicketId={ticketId ?? "N/A"}");
                }
                else
                {
                    _state = UploadState.Failed;
                    _statusMessage = $"上传失败：{result.Error}";
                    Log.Error($"FeedbackWindow: upload failed — {result.Error}");
                }
            }

            // ── 私有工具方法 ──────────────────────────────────────────────

            /// <summary>将 NLog 日志目录下所有 .log 文件打包为 ZIP 字节数组（线程池执行）。</summary>
            private static byte[] PackLogsToZip()
            {
                string logDir = NLogManager.logDirectory;
                if (!Directory.Exists(logDir))
                    throw new DirectoryNotFoundException($"日志目录不存在：{logDir}");

                string[] logFiles = Directory.GetFiles(logDir, "*.log", SearchOption.TopDirectoryOnly);
                if (logFiles.Length == 0)
                    throw new FileNotFoundException("日志目录中未找到任何 .log 文件。");

                using var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (string filePath in logFiles)
                    {
                        var entry = zip.CreateEntry(
                            Path.GetFileName(filePath),
                            System.IO.Compression.CompressionLevel.Optimal);

                        using var entryStream = entry.Open();
                        // 以共享读方式打开，避免与 NLog 写入时的文件锁冲突
                        using var fs = new FileStream(
                            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        fs.CopyTo(entryStream);
                    }
                }
                return ms.ToArray();
            }

            /// <summary>构建反馈元数据 JSON（含描述、严重程度、设备信息等）。</summary>
            private static string BuildFeedbackMeta(string description, string severity)
            {
                var sb = new StringBuilder();
                sb.Append('{');
                Append(sb, "description", description ?? string.Empty);         sb.Append(',');
                Append(sb, "severity",    severity    ?? "normal");             sb.Append(',');
                Append(sb, "userId",      SystemInfo.deviceUniqueIdentifier);   sb.Append(',');
                Append(sb, "appVersion",  Application.version);                 sb.Append(',');
                Append(sb, "platform",    Application.platform.ToString());     sb.Append(',');
                Append(sb, "deviceModel", SystemInfo.deviceModel);              sb.Append(',');
                Append(sb, "osVersion",   SystemInfo.operatingSystem);          sb.Append(',');
                Append(sb, "scene",       SceneManager.GetActiveScene().name);  sb.Append(',');
                Append(sb, "reportTime",  DateTime.UtcNow.ToString("o"));
                sb.Append('}');
                return sb.ToString();

                // 局部函数：安全转义字符串字段
                static void Append(StringBuilder s, string key, string val)
                {
                    s.Append('"').Append(key).Append("\":\"");
                    if (val != null)
                        s.Append(val
                            .Replace("\\", "\\\\")
                            .Replace("\"", "\\\"")
                            .Replace("\n",  "\\n")
                            .Replace("\r",  "\\r"));
                    s.Append('"');
                }
            }

            /// <summary>反馈上传服务端响应体。</summary>
            private sealed class FeedbackServerResponse
            {
                public string TicketId { get; set; }
                public string Message  { get; set; }
            }
        }
    }
}
