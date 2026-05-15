// ============================================================
// WebRequestService.cs
// 对标 GameFramework.WebRequest.WebRequestManager。
//
// 核心机制：
//
//  1. 优先级调度（对标 GameFramework TaskPool + Agent 池）
//     ── SortedList<(negPriority, serialId), UniTaskCompletionSource<bool>>
//        Key = (-priority, serialId)：自然升序 ≡ 优先级降序 + FIFO 同优先级
//        当并发槽已满时，请求进入此队列；空闲槽直接转移给队首等待者。
//
//  2. 并发控制（对标 Agent 总数 / WorkingAgentCount）
//     ── _activeCount 计数器 + MaxConcurrency 上限
//        槽位在任务间"直接转移"，避免 _activeCount 反复增减抖动。
//
//  3. 取消安全（对标 GameFramework RemoveWebRequest）
//     ── 每个任务持有独立 TaskCts（serialId 取消）+ 联合 LinkedCts（HTTP 调用层）
//        SlotAcquired 标志区分"在队列中被取消"与"执行中被取消"，
//        确保只有真正持有槽的任务才会执行 Release 逻辑。
//
//  4. 超时（对标 IWebRequestManager.Timeout）
//     ── LinkedCts.CancelAfter(timeout) 统一为所有 HTTP 方法提供超时，
//        底层 HTTP 实现细节对业务层透明。
//
//  5. 生命周期（由 VContainer 管理，IDisposable → CancelAll + 资源释放）
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MyGame.Toolkit.Network.Internal;
using NLog;
using UnityEngine.Networking;

namespace MyGame.Toolkit.Network
{
    /// <summary>
    /// Web 请求服务实现。通过 VContainer 以 Singleton 注册，全局共享。
    /// </summary>
    public sealed class WebRequestService : IWebRequestService, IDisposable
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // ── 配置 ──────────────────────────────────────────────────────────
        private int _maxConcurrency;
        private int _defaultTimeout;

        // ── 任务注册表（对标 GameFramework TaskPool 内部 task 列表）────────
        // key = SerialId
        private readonly Dictionary<int, WebRequestTask>      _taskRegistry = new();
        // key = Tag → serialId 集合（快速按标签查询与取消）
        private readonly Dictionary<string, HashSet<int>>     _tagIndex     = new();

        // ── 优先级调度队列（对标 GameFramework 的等待 Task 队列）────────────
        // Key: (-priority, serialId) → SortedList 自然升序等于优先级降序 + FIFO
        private int _activeCount = 0;
        private readonly SortedList<(int NegPriority, int SerialId), UniTaskCompletionSource<bool>>
            _waitQueue = new();

        // ── 公开属性 ──────────────────────────────────────────────────────

        public int MaxConcurrency
        {
            get => _maxConcurrency;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaxConcurrency must be >= 1.");
                _maxConcurrency = value;
            }
        }

        public int DefaultTimeout
        {
            get => _defaultTimeout;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "DefaultTimeout must be >= 1 second.");
                _defaultTimeout = value;
            }
        }

        public int ActiveRequestCount  => _activeCount;
        public int WaitingRequestCount => _waitQueue.Count;
        public int TotalRequestCount   => _taskRegistry.Count;

        // ── 构造 ──────────────────────────────────────────────────────────

        public WebRequestService(
            int maxConcurrency = WebRequestConstant.DefaultMaxConcurrency,
            int defaultTimeout = WebRequestConstant.DefaultTimeout)
        {
            _maxConcurrency = maxConcurrency;
            _defaultTimeout = defaultTimeout;
        }

        // ── HTTP GET ──────────────────────────────────────────────────────

        public async UniTask<WebRequestResult<T>> GetAsync<T>(
            string domain,
            string path,
            WebRequestOptions options           = null,
            CancellationToken cancellationToken = default)
        {
            var task = CreateAndRegisterTask(domain + path, options, cancellationToken, out var linkedCt);

            try
            {
                await AcquireSlotAsync(task, linkedCt);
                task.Status = WebRequestTaskStatus.Running;

                // 超时通过 linkedCt.CancelAfter 统一管理，UnityWebRequest timeout 设为 0（关闭）
                var result = await WebRequestServiceHelper.GetAsync<T>(
                    domain, path,
                    token:             options?.Token,
                    parameters:        options?.QueryParams,
                    headers:           options?.Headers,
                    timeout:           0,
                    cancellationToken: linkedCt);

                task.Status = result.Success
                    ? WebRequestTaskStatus.Succeeded
                    : WebRequestTaskStatus.Failed;
                return result;
            }
            catch (OperationCanceledException)
            {
                task.Status = WebRequestTaskStatus.Cancelled;
                Log.Info($"[WebRequest] GET cancelled  SerialId={task.SerialId}  Uri={task.Uri}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            finally
            {
                ReleaseSlotAndUnregister(task);
            }
        }

        // ── HTTP POST (JSON body) ──────────────────────────────────────────

        public async UniTask<WebRequestResult<T>> PostAsync<T>(
            string domain,
            string path,
            object body,
            WebRequestOptions options           = null,
            CancellationToken cancellationToken = default)
        {
            var task = CreateAndRegisterTask(domain + path, options, cancellationToken, out var linkedCt);

            try
            {
                await AcquireSlotAsync(task, linkedCt);
                task.Status = WebRequestTaskStatus.Running;

                var result = await WebRequestServiceHelper.PostAsync<T>(
                    domain, path,
                    token:             options?.Token,
                    data:              body,
                    headers:           options?.Headers,
                    cancellationToken: linkedCt);

                task.Status = result.Success
                    ? WebRequestTaskStatus.Succeeded
                    : WebRequestTaskStatus.Failed;
                return result;
            }
            catch (OperationCanceledException)
            {
                task.Status = WebRequestTaskStatus.Cancelled;
                Log.Info($"[WebRequest] POST cancelled  SerialId={task.SerialId}  Uri={task.Uri}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            finally
            {
                ReleaseSlotAndUnregister(task);
            }
        }

        // ── HTTP POST (raw bytes body) ─────────────────────────────────────

        public async UniTask<WebRequestResult<T>> PostRawAsync<T>(
            string domain,
            string path,
            byte[] rawBody,
            WebRequestOptions options           = null,
            CancellationToken cancellationToken = default)
        {
            var task = CreateAndRegisterTask(domain + path, options, cancellationToken, out var linkedCt);

            try
            {
                await AcquireSlotAsync(task, linkedCt);
                task.Status = WebRequestTaskStatus.Running;

                var result = await WebRequestServiceHelper.PostWithQueryParamsAsync<T>(
                    domain, path,
                    token:             options?.Token,
                    parameters:        options?.QueryParams,
                    bodyData:          rawBody,
                    cancellationToken: linkedCt);

                task.Status = result.Success
                    ? WebRequestTaskStatus.Succeeded
                    : WebRequestTaskStatus.Failed;
                return result;
            }
            catch (OperationCanceledException)
            {
                task.Status = WebRequestTaskStatus.Cancelled;
                Log.Info($"[WebRequest] POST(raw) cancelled  SerialId={task.SerialId}  Uri={task.Uri}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            finally
            {
                ReleaseSlotAndUnregister(task);
            }
        }

        // ── HTTP POST (multipart/form-data) ───────────────────────────────

        public async UniTask<WebRequestResult<T>> PostMultipartAsync<T>(
            string url,
            List<IMultipartFormSection> formSections,
            WebRequestOptions options           = null,
            CancellationToken cancellationToken = default)
        {
            var task = CreateAndRegisterTask(url, options, cancellationToken, out var linkedCt);

            try
            {
                await AcquireSlotAsync(task, linkedCt);
                task.Status = WebRequestTaskStatus.Running;

                var timeout = options?.Timeout > 0 ? options.Timeout : 0;
                var result = await WebRequestServiceHelper.PostMultipartAsync<T>(
                    url,
                    token:             options?.Token,
                    formSections:      formSections,
                    timeout:           timeout,
                    cancellationToken: linkedCt);

                task.Status = result.Success
                    ? WebRequestTaskStatus.Succeeded
                    : WebRequestTaskStatus.Failed;
                return result;
            }
            catch (OperationCanceledException)
            {
                task.Status = WebRequestTaskStatus.Cancelled;
                Log.Info($"[WebRequest] POST(multipart) cancelled  SerialId={task.SerialId}  Uri={task.Uri}");
                return WebRequestResult<T>.Fail("Request cancelled");
            }
            finally
            {
                ReleaseSlotAndUnregister(task);
            }
        }

        // ── 任务查询 ──────────────────────────────────────────────────────

        public WebRequestTaskInfo GetTaskInfo(int serialId)
        {
            return _taskRegistry.TryGetValue(serialId, out var task)
                ? task.ToInfo()
                : WebRequestTaskInfo.Invalid;
        }

        public WebRequestTaskInfo[] GetTaskInfosByTag(string tag)
        {
            if (!_tagIndex.TryGetValue(tag, out var ids) || ids.Count == 0)
                return Array.Empty<WebRequestTaskInfo>();

            var result = new WebRequestTaskInfo[ids.Count];
            int i = 0;
            foreach (int id in ids)
                if (_taskRegistry.TryGetValue(id, out var t))
                    result[i++] = t.ToInfo();
            return result;
        }

        public void GetTaskInfosByTag(string tag, List<WebRequestTaskInfo> results)
        {
            results.Clear();
            if (!_tagIndex.TryGetValue(tag, out var ids)) return;
            foreach (int id in ids)
                if (_taskRegistry.TryGetValue(id, out var t))
                    results.Add(t.ToInfo());
        }

        // ── 取消控制 ──────────────────────────────────────────────────────

        public bool CancelRequest(int serialId)
        {
            if (!_taskRegistry.TryGetValue(serialId, out var task)) return false;
            task.TaskCts.Cancel();
            return true;
        }

        public int CancelRequestsByTag(string tag)
        {
            if (!_tagIndex.TryGetValue(tag, out var ids) || ids.Count == 0) return 0;

            // 复制快照，避免在取消回调中 _tagIndex 被修改时引发集合修改异常
            var snapshot = new int[ids.Count];
            ids.CopyTo(snapshot);

            int count = 0;
            foreach (int id in snapshot)
                if (_taskRegistry.TryGetValue(id, out var t))
                {
                    t.TaskCts.Cancel();
                    count++;
                }
            return count;
        }

        public void CancelAll()
        {
            var snapshot = new WebRequestTask[_taskRegistry.Count];
            _taskRegistry.Values.CopyTo(snapshot, 0);
            foreach (var task in snapshot)
                task.TaskCts.Cancel();
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            CancelAll();
        }

        // ── 私有核心逻辑 ──────────────────────────────────────────────────

        /// <summary>
        /// 创建任务并注册到任务表与标签索引。
        /// 同时构建联合 CancellationToken 并按 Timeout 设置自动取消。
        /// </summary>
        private WebRequestTask CreateAndRegisterTask(
            string fullUri,
            WebRequestOptions options,
            CancellationToken callerCt,
            out CancellationToken linkedCt)
        {
            int timeout   = options?.Timeout > 0 ? options.Timeout : _defaultTimeout;
            var taskCts   = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(taskCts.Token, callerCt);

            // 超时：通过 CancelAfter 统一覆盖所有 HTTP 方法（GET / POST / PostRaw）
            linkedCts.CancelAfter(TimeSpan.FromSeconds(timeout));

            linkedCt = linkedCts.Token;

            var task = new WebRequestTask(fullUri, options, timeout, taskCts, linkedCts);

            _taskRegistry[task.SerialId] = task;

            if (!string.IsNullOrEmpty(task.Tag))
            {
                if (!_tagIndex.TryGetValue(task.Tag, out var set))
                {
                    set = new HashSet<int>();
                    _tagIndex[task.Tag] = set;
                }
                set.Add(task.SerialId);
            }

            Log.Debug($"[WebRequest] Created  SerialId={task.SerialId}  Uri={task.Uri}  " +
                      $"Tag={task.Tag}  Priority={task.Priority}  Timeout={task.Timeout}s");
            return task;
        }

        /// <summary>
        /// 优先级调度：若并发槽未满则立即获取；否则进入优先级等待队列直到有空闲槽。
        /// <para>
        /// 队列 Key = (-priority, serialId)，SortedList 自然升序即：
        /// 优先级最高者排队首（negPriority 最小），同优先级按 serialId 升序（FIFO）。
        /// </para>
        /// <para>
        /// 取消安全：ct.Register 在取消时从队列中移除本任务的等待节点，
        /// 避免槽位被错误转移给已取消的等待者。
        /// </para>
        /// </summary>
        private async UniTask AcquireSlotAsync(WebRequestTask task, CancellationToken ct)
        {
            // 快速路径：有空闲槽，直接获取
            if (_activeCount < _maxConcurrency)
            {
                _activeCount++;
                task.SlotAcquired = true;
                return;
            }

            // 慢速路径：进入优先级等待队列
            var tcs = new UniTaskCompletionSource<bool>();
            var key = (-task.Priority, task.SerialId);
            _waitQueue.Add(key, tcs);

            // 注册取消回调：从队列中摘除本节点，避免已取消的任务占据队列位置
            using var reg = ct.Register(() =>
            {
                if (_waitQueue.Remove(key))
                    tcs.TrySetCanceled();
            });

            // 等待上游任务完成时通过 TrySetResult 传递槽位
            await tcs.Task;

            // 执行到此处说明 TrySetResult 已触发（slot 由 ReleaseSlotAndUnregister 直接转移过来）
            task.SlotAcquired = true;
        }

        /// <summary>
        /// 释放并发槽并清理任务注册信息。
        /// <para>
        /// 槽位转移策略：若等待队列非空，将槽位直接转移给优先级最高的等待者
        /// （_activeCount 不变），避免计数器抖动；队列为空则直接递减。
        /// </para>
        /// <para>
        /// 若任务在等待阶段被取消（SlotAcquired == false），则不执行槽位释放逻辑，
        /// 防止将从未持有的槽意外归还或转移。
        /// </para>
        /// </summary>
        private void ReleaseSlotAndUnregister(WebRequestTask task)
        {
            // 1. 从注册表中移除（先于 Dispose，防止取消回调访问已释放资源）
            _taskRegistry.Remove(task.SerialId);

            if (!string.IsNullOrEmpty(task.Tag) && _tagIndex.TryGetValue(task.Tag, out var set))
            {
                set.Remove(task.SerialId);
                if (set.Count == 0) _tagIndex.Remove(task.Tag);
            }

            // 2. 暂存 SlotAcquired（Dispose 后不可读）
            bool slotAcquired = task.SlotAcquired;

            Log.Debug($"[WebRequest] Done  SerialId={task.SerialId}  Status={task.Status}  " +
                      $"SlotAcquired={slotAcquired}");

            // 3. 释放 CTS 资源
            task.Dispose();

            // 4. 槽位逻辑：仅在曾持有槽时才转移或归还
            if (!slotAcquired)
                return;

            if (_waitQueue.Count > 0)
            {
                // 将槽直接转移给队首（优先级最高）的等待者
                var nextTcs = _waitQueue.Values[0];
                _waitQueue.RemoveAt(0);
                nextTcs.TrySetResult(true);
                // _activeCount 不变：槽位转移，非释放
            }
            else
            {
                _activeCount--;
            }
        }
    }
}
