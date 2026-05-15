using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using UnityEngine;
using UnityEngine.Networking;

namespace MyGame.Toolkit.Network.Internal
{
    /// <summary>
    /// 基于 UniTask 的 HTTP 工具类（内部实现细节）。
    /// 业务层请统一通过 IWebRequestService 发起请求。
    /// </summary>
    internal static class WebRequestServiceHelper
    {
        private static readonly NLog.Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>忽略 SSL 证书校验，用于内网/自签名证书场景。</summary>
        private sealed class IgnoreCertHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => true;
        }

        #region Private Helpers

        /// <summary>拼接 domain + path，并将 parameters 追加为 URL 查询字符串。</summary>
        private static string BuildUrl(string domain, string path, Dictionary<string, string> parameters)
        {
            var sb = new StringBuilder(domain).Append(path);
            if (parameters != null && parameters.Count > 0)
            {
                sb.Append('?');
                bool first = true;
                foreach (var kv in parameters)
                {
                    if (!first) sb.Append('&');
                    sb.Append(kv.Key).Append('=').Append(UnityWebRequest.EscapeURL(kv.Value));
                    first = false;
                }
            }
            return sb.ToString();
        }

        /// <summary>设置 Authorization header 和证书处理器。</summary>
        private static void SetAuthHeader(UnityWebRequest request, string token)
        {
            if (!string.IsNullOrEmpty(token))
                request.SetRequestHeader("Authorization", "Bearer " + token);
            request.certificateHandler = new IgnoreCertHandler();
        }

        /// <summary>统一解析 JSON 响应：检查网络错误 → 反序列化 → 返回结果。</summary>
        private static WebRequestResult<T> ParseJsonResponse<T>(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.Error($"Request failed: {request.error} | URL: {request.url}");
                return WebRequestResult<T>.Fail(request.error);
            }

            string json = request.downloadHandler?.text;
            if (string.IsNullOrEmpty(json))
                return WebRequestResult<T>.Fail("Empty response body");

            try
            {
                T data = JsonConvert.DeserializeObject<T>(json);
                return WebRequestResult<T>.Ok(data);
            }
            catch (Exception ex)
            {
                Log.Error($"JSON parse error: {ex.Message}");
                return WebRequestResult<T>.Fail($"JSON Error: {ex.Message}");
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 检测指定 URL 是否可达（先 HEAD，失败后降级 GET）。
        /// </summary>
        public static async UniTask<bool> CheckUrlAccessibilityAsync(
            string url,
            float timeout,
            CancellationToken cancellationToken = default)
        {
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Log.Info("No network reachability.");
                return false;
            }

            bool headResult = await TryHeadRequestAsync(url, timeout, cancellationToken);
            if (headResult) return true;

            // HEAD 失败或超时时，检查是否是外部取消（若是则不继续降级）
            cancellationToken.ThrowIfCancellationRequested();

            Log.Warn($"HEAD failed for {url}, falling back to GET.");
            return await TryGetAccessibilityAsync(url, timeout, cancellationToken);
        }

        /// <summary>
        /// 发起 GET 请求，自动将 parameters 序列化为查询字符串，将响应反序列化为 T。
        /// </summary>
        public static async UniTask<WebRequestResult<T>> GetAsync<T>(
            string domain,
            string url,
            string token,
            Dictionary<string, string> parameters = null,
            Dictionary<string, string> headers = null,
            int timeout = 10,
            CancellationToken cancellationToken = default)
        {
            string fullUrl = BuildUrl(domain, url, parameters);
            using var request = UnityWebRequest.Get(fullUrl);
            request.timeout = timeout;
            request.SetRequestHeader("Content-Type", "application/json");
            SetAuthHeader(request, token);

            if (headers != null)
                foreach (var kv in headers)
                    request.SetRequestHeader(kv.Key, kv.Value);

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"GET request cancelled: {fullUrl}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            catch (UnityWebRequestException ex)
            {
                Log.Error($"GET request error: {ex.Message} | URL: {fullUrl}");
                return WebRequestResult<T>.Fail(ex.Message);
            }

            return ParseJsonResponse<T>(request);
        }

        /// <summary>
        /// 发起 POST 请求，将 data 序列化为 JSON body，将响应反序列化为 T。
        /// </summary>
        public static async UniTask<WebRequestResult<T>> PostAsync<T>(
            string domain,
            string url,
            string token,
            object data,
            Dictionary<string, string> headers = null,
            CancellationToken cancellationToken = default)
        {
            string json = data != null ? JsonConvert.SerializeObject(data) : string.Empty;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(domain + url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            SetAuthHeader(request, token);

            if (headers != null)
                foreach (var kv in headers)
                    request.SetRequestHeader(kv.Key, kv.Value);

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"POST request cancelled: {domain + url}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            catch (UnityWebRequestException ex)
            {
                Log.Error($"POST request error: {ex.Message}");
                return WebRequestResult<T>.Fail(ex.Message);
            }

            return ParseJsonResponse<T>(request);
        }

        /// <summary>
        /// 发起 POST 请求，parameters 追加到 URL，bodyData 作为原始 byte[] body。
        /// </summary>
        public static async UniTask<WebRequestResult<T>> PostWithQueryParamsAsync<T>(
            string domain,
            string url,
            string token,
            Dictionary<string, string> parameters,
            byte[] bodyData,
            CancellationToken cancellationToken = default)
        {
            string fullUrl = BuildUrl(domain, url, parameters);
            Log.Info($"POST URL: {fullUrl}");

            using var request = new UnityWebRequest(fullUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyData);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            SetAuthHeader(request, token);

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"POST request cancelled: {fullUrl}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            catch (UnityWebRequestException ex)
            {
                Log.Error($"POST request error: {ex.Message}");
                return WebRequestResult<T>.Fail(ex.Message);
            }

            return ParseJsonResponse<T>(request);
        }

        /// <summary>
        /// 发起 Multipart POST 请求，将 formSections 作为 multipart/form-data 上传，将响应反序列化为 T。
        /// </summary>
        public static async UniTask<WebRequestResult<T>> PostMultipartAsync<T>(
            string url,
            string token,
            List<IMultipartFormSection> formSections,
            int timeout = 30,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Post(url, formSections);
            request.timeout = timeout;
            SetAuthHeader(request, token);

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Log.Warn($"POST multipart cancelled: {url}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            catch (UnityWebRequestException ex)
            {
                Log.Error($"POST multipart error: {ex.Message} | URL: {url}");
                return WebRequestResult<T>.Fail(ex.Message);
            }

            return ParseJsonResponse<T>(request);
        }

        /// <summary>
        /// 下载文件到本地路径，支持进度回调（需调用方提供 totalLength）。
        /// </summary>
        public static async UniTask<bool> DownloadAsync(
            string domain,
            string url,
            string savePath,
            long totalLength,
            Dictionary<string, string> parameters = null,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            string fullUrl = BuildUrl(domain, url, parameters);
            using var request = UnityWebRequest.Get(fullUrl);
            request.downloadHandler = new DownloadHandlerFile(savePath, false);
            request.certificateHandler = new IgnoreCertHandler();
            request.SetRequestHeader("Content-Type", "application/octet-stream");

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    return false;
                }
                if (totalLength > 0)
                    progress?.Report((float)request.downloadedBytes / totalLength);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Log.Error($"DownloadAsync failed: {request.error}");
                return false;
            }

            progress?.Report(1f);
            return true;
        }

        /// <summary>
        /// 支持断点续传的文件下载：先 HEAD 获取大小和 Range 支持情况，再 GET 下载。
        /// </summary>
        public static async UniTask<bool> DownloadWithResumeAsync(
            string domain,
            string url,
            string token,
            string savePath,
            Dictionary<string, string> parameters = null,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            string fullUrl = BuildUrl(domain, url, parameters);
            Log.Info($"DownloadWithResume: {fullUrl}");

            long downloadedBytes = 0;
            bool fileExists = File.Exists(savePath);
            if (fileExists)
            {
                downloadedBytes = new FileInfo(savePath).Length;
                Log.Info($"Resuming from {downloadedBytes} bytes.");
            }

            // HEAD：获取文件总大小和 Range 支持情况
            long totalLength = 0;
            bool supportsRange = false;
            using (var headRequest = UnityWebRequest.Head(fullUrl))
            {
                SetAuthHeader(headRequest, token);
                try
                {
                    await headRequest.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

                    string acceptRanges = headRequest.GetResponseHeader("Accept-Ranges");
                    supportsRange = string.Equals(acceptRanges, "bytes", StringComparison.OrdinalIgnoreCase);
                    long.TryParse(headRequest.GetResponseHeader("Content-Length"), out totalLength);
                    Log.Info($"File size: {totalLength}, Range support: {supportsRange}");

                    if (fileExists && totalLength > 0 && downloadedBytes >= totalLength)
                    {
                        Log.Info("File already fully downloaded.");
                        return true;
                    }
                }
                catch (UnityWebRequestException ex)
                {
                    Log.Warn($"HEAD failed: {ex.Message}");
                }
            }

            // GET：正式下载（断点续传时追加写入）
            using var request = UnityWebRequest.Get(fullUrl);
            SetAuthHeader(request, token);

            if (supportsRange && downloadedBytes > 0)
            {
                request.SetRequestHeader("Range", $"bytes={downloadedBytes}-");
                Log.Info($"Range header: bytes={downloadedBytes}-");
            }

            request.downloadHandler = new DownloadHandlerFile(savePath, fileExists && downloadedBytes > 0);

            var op = request.SendWebRequest();
            while (!op.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Log.Info("Download cancelled.");
                    request.Abort();
                    return false;
                }
                if (totalLength > 0)
                    progress?.Report((float)(downloadedBytes + (long)request.downloadedBytes) / totalLength);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                Log.Info($"Download complete: {downloadedBytes + (long)request.downloadedBytes} bytes");
                progress?.Report(1f);
                return true;
            }

            Log.Error($"Download failed: {request.error}");
            return false;
        }

        #endregion

        #region Private Methods

        /// <summary>发送 HEAD 请求并在超时或失败时安静返回 false。</summary>
        private static async UniTask<bool> TryHeadRequestAsync(
            string url, float timeout, CancellationToken cancellationToken)
        {
            // 创建联动 token：外部取消 OR 超时均会触发
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfterSlim(TimeSpan.FromSeconds(timeout));

            using var request = UnityWebRequest.Head(url);
            request.certificateHandler = new IgnoreCertHandler();

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cts.Token);

                bool success = request.result == UnityWebRequest.Result.Success;
                if (success)
                    Log.Info($"HEAD success: {url} | Code: {request.responseCode}");
                else
                    Log.Warn($"HEAD failed: {request.error} | Code: {request.responseCode}");
                return success;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 超时（非外部取消），静默返回 false 触发降级
                Log.Warn($"HEAD timed out ({timeout}s) for {url}");
                return false;
            }
            catch (UnityWebRequestException ex)
            {
                Log.Warn($"HEAD exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>发送 GET 可达性探测请求，超时或失败时返回 false。</summary>
        private static async UniTask<bool> TryGetAccessibilityAsync(
            string url, float timeout, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfterSlim(TimeSpan.FromSeconds(timeout));

            using var request = UnityWebRequest.Get(url);
            request.certificateHandler = new IgnoreCertHandler();

            try
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: cts.Token);

                bool success = request.result == UnityWebRequest.Result.Success;
                Log.Info($"GET {(success ? "success" : "failed")}: {url} | Code: {request.responseCode}");
                return success;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warn($"GET timed out ({timeout}s) for {url}");
                return false;
            }
            catch (UnityWebRequestException ex)
            {
                Log.Warn($"GET exception: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
