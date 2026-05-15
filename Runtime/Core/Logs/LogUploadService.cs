using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MyGame.Toolkit.Network;
using NLog;
using UnityEngine;
using UnityEngine.Networking;

namespace GameFramework
{
    /// <summary>
    /// 日志主动上报服务。将本地 NLog 日志文件打包为 ZIP，通过 multipart POST 上传到服务器。
    /// </summary>
    /// <remarks>
    /// 典型用法：在"反馈问题"按钮的回调中调用 <see cref="UploadAsync"/>。
    /// 服务端约定：接受 multipart/form-data，字段名 "file"（ZIP）和 "meta"（JSON），
    /// 响应体示例：{"ticketId":"20240429-001","message":"ok"}
    /// </remarks>
    public static class LogUploadService
    {
        private static readonly NLog.Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>日志上报结果。</summary>
        public readonly struct UploadResult
        {
            /// <summary>是否上报成功。</summary>
            public bool Success { get; }

            /// <summary>失败时的错误描述。</summary>
            public string Error { get; }

            private UploadResult(bool success, string error)
            {
                Success = success;
                Error = error;
            }

            /// <summary>构造成功结果。</summary>
            public static UploadResult Ok() => new UploadResult(true, null);

            /// <summary>构造失败结果。</summary>
            public static UploadResult Fail(string error) => new UploadResult(false, error);
        }

        /// <summary>
        /// 收集本地日志文件，打包为 ZIP，通过 multipart POST 上传到指定服务器。
        /// </summary>
        /// <param name="uploadUrl">日志接收接口完整 URL，例如 https://your-server/api/logs/upload。</param>
        /// <param name="token">鉴权 token，传 null 或空字符串则不附加 Authorization 头。</param>
        /// <param name="userId">当前用户标识，附加到上报元数据中，便于后台按用户查询。</param>
        /// <param name="reason">上报原因标签，如 "manual_feedback"、"auto_exception"。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>上报结果，包含服务端返回的工单 ID（若服务端支持）。</returns>
        public static async UniTask<UploadResult> UploadAsync(
            IWebRequestService webRequestService,
            string uploadUrl,
            string token,
            string userId,
            string reason = "manual_feedback",
            CancellationToken cancellationToken = default)
        {
            if (webRequestService == null)
                return UploadResult.Fail("webRequestService is null");

            if (string.IsNullOrEmpty(uploadUrl))
                return UploadResult.Fail("uploadUrl is null or empty");

            // 1. 收集日志文件
            if (!Directory.Exists(NLogManager.logDirectory))
                return UploadResult.Fail("Log directory does not exist");

            string[] logFiles = Directory.GetFiles(NLogManager.logDirectory, "*.log", SearchOption.TopDirectoryOnly);
            if (logFiles.Length == 0)
                return UploadResult.Fail("No log files found");

            Log.Info($"LogUpload: collecting {logFiles.Length} log file(s), reason={reason}");

            // 2. 在线程池打包 ZIP，避免卡主线程
            byte[] zipBytes;
            try
            {
                zipBytes = await UniTask.RunOnThreadPool(
                    () => PackLogsToZip(logFiles),
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return UploadResult.Fail("Upload cancelled");
            }
            catch (Exception ex)
            {
                Log.Error($"LogUpload: pack failed — {ex.Message}");
                return UploadResult.Fail($"Pack failed: {ex.Message}");
            }

            // 3. 构建元数据 JSON（设备信息 + 用户信息）
            string metaJson = BuildMetaJson(userId, reason);
            string fileName = $"logs_{SanitizeFileName(userId)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

            // 4. Multipart POST 上传（统一通过 IWebRequestService）
            var formSections = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("meta", metaJson, "application/json"),
                new MultipartFormFileSection("file", zipBytes, fileName, "application/zip")
            };

            var options = new WebRequestOptions
            {
                Tag = "log-upload",
                Token = token,
                Timeout = 30,
            };

            var (uploadSuccess, serverResponse, uploadError) =
                await webRequestService.PostMultipartAsync<UploadServerResponse>(
                    uploadUrl,
                    formSections,
                    options,
                    cancellationToken);

            if (!uploadSuccess)
            {
                Log.Error($"LogUpload: upload failed — {uploadError}");
                return UploadResult.Fail(uploadError);
            }

            // 5. 取服务端返回的工单 ID（服务端不返回时为 null）
            string ticketId = serverResponse?.TicketId;
            Log.Info($"LogUpload: success. TicketId={ticketId ?? "N/A"}");
            return UploadResult.Ok();
        }

        // ─────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────

        /// <summary>将多个日志文件打包为 ZIP 字节数组（供线程池调用）。</summary>
        private static byte[] PackLogsToZip(string[] logFiles)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string filePath in logFiles)
                {
                    string entryName = Path.GetFileName(filePath);
                    var entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    // 以共享读取方式打开，避免与 NLog 写入时的文件锁冲突
                    using var fs = new FileStream(
                        filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    fs.CopyTo(entryStream);
                }
            }
            return ms.ToArray();
        }

        /// <summary>构建上报元数据 JSON（手动拼接，避免引入额外程序集引用）。</summary>
        private static string BuildMetaJson(string userId, string reason)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendJsonField(sb, "userId", userId);        sb.Append(',');
            AppendJsonField(sb, "reason", reason);        sb.Append(',');
            AppendJsonField(sb, "appVersion", Application.version);     sb.Append(',');
            AppendJsonField(sb, "platform", Application.platform.ToString()); sb.Append(',');
            AppendJsonField(sb, "deviceModel", SystemInfo.deviceModel); sb.Append(',');
            AppendJsonField(sb, "osVersion", SystemInfo.operatingSystem); sb.Append(',');
            AppendJsonField(sb, "uploadTime", DateTime.UtcNow.ToString("o"));
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendJsonField(StringBuilder sb, string key, string value)
        {
            sb.Append('"').Append(key).Append("\":\"");
            if (value != null)
                sb.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.Append('"');
        }

        /// <summary>清理文件名中不合法字符，防止路径注入。</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        /// <summary>日志上传接口响应体。</summary>
        private sealed class UploadServerResponse
        {
            public string TicketId { get; set; }
        }
    }
}
