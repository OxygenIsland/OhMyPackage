# TaskPool 任务池模块解析

> 基于 GameFramework (https://gameframework.cn/) —— 作者：Jiang Yin (2013-2021)

---

## 一、模块概述

TaskPool（任务池）是 GameFramework 框架中的**基础任务调度模块**，实现了一个基于**代理（Agent）模式**的轻量级任务队列系统。它的核心思想是：**将"需要做的事情（Task）"和"执行事情的工人（Agent）"解耦**，通过一个池化的调度器来管理任务的排队、分发和执行。

简单来说，TaskPool 就是一个**带优先级的生产者-消费者模型**：
- **生产者**：外部代码不断提交任务（Task）到等待队列。
- **消费者**：有限数量的代理（Agent）从队列中取出任务并执行。
- **调度器**：TaskPool 本身负责在每一帧轮询中，把等待的任务分配给空闲的代理。

---

## 二、文件结构与职责

| 文件 | 职责 |
|---|---|
| `TaskPool.cs` | 核心调度器。管理空闲代理栈、工作中代理链表、等待任务链表，负责每帧轮询分发任务 |
| `TaskBase.cs` | 任务基类。定义了 SerialId（序列号）、Tag（标签）、Priority（优先级）、UserData（用户数据）、Done（完成标记）等通用属性，实现了 `IReference` 接口以支持引用池回收 |
| `ITaskAgent.cs` | 任务代理接口。定义了 `Initialize()`、`Update()`、`Shutdown()`、`Start(task)`、`Reset()` 等生命周期方法 |
| `TaskInfo.cs` | 任务信息结构体。只读的快照数据，用于外部查询任务状态，不暴露内部引用 |
| `TaskStatus.cs` | 任务状态枚举：`Todo`（未开始）、`Doing`（执行中）、`Done`（已完成） |
| `StartTaskStatus.cs` | 启动任务状态枚举：`Done`（立即完成）、`CanResume`（可继续执行）、`HasToWait`（需等待依赖）、`UnknownError`（未知错误） |

---

## 三、核心机制详解

### 3.1 数据结构

```
┌─────────────────────────────────────────────┐
│                  TaskPool<T>                │
│                                             │
│  m_FreeAgents   : Stack<ITaskAgent<T>>      │  ← 空闲代理栈（后进先出）
│  m_WorkingAgents: LinkedList<ITaskAgent<T>> │  ← 工作中代理链表
│  m_WaitingTasks : LinkedList<T>             │  ← 等待任务队列（按优先级排序）
│  m_Paused       : bool                      │  ← 暂停标记
└─────────────────────────────────────────────┘
```

### 3.2 任务添加（优先级插入）

`AddTask()` 采用**插入排序**的方式，从等待队列**尾部向前遍历**，找到第一个优先级 >= 新任务的节点，将新任务插入其后。这保证了：
- 高优先级任务排在队列前面，优先被执行。
- 同优先级的任务保持 FIFO（先进先出）顺序。

### 3.3 每帧轮询（Update）

每帧调用 `Update()` 时执行两步：

1. **ProcessRunningTasks**：遍历工作中的代理，调用其 `Update()` 推进任务。如果任务标记为 `Done`，则回收代理和任务。
2. **ProcessWaitingTasks**：遍历等待队列，只要有空闲代理，就取出一个代理去 `Start()` 任务。根据 `StartTaskStatus` 的返回值决定：
   - `Done`：任务立即完成，回收代理和任务。
   - `CanResume`：任务需要持续执行，代理保持工作状态。
   - `HasToWait`：代理无法处理此任务（比如依赖未满足），回收代理，任务留在队列。
   - `UnknownError`：出错，回收代理和任务。

### 3.4 对象池集成

TaskBase 实现了 `IReference` 接口，配合框架的 `ReferencePool` 实现**零 GC 的对象复用**。任务完成后不是被销毁（new/destroy），而是被清理后放回引用池，下次使用时直接取出复用。

### 3.5 暂停与恢复

通过 `Paused` 属性可以随时暂停/恢复任务池。暂停后 `Update()` 会直接返回，不再推进任何任务。

---

## 四、行业常见使用示例：资源下载管理器

以**游戏热更新时的资源下载**为例，这是 TaskPool 最典型的应用场景：

### 场景描述

游戏启动时需要从 CDN 下载 200 个资源包，但不能同时开 200 个 HTTP 连接（会被服务器限流、占用过多带宽）。我们需要：
- 控制并发数（比如最多同时下载 3 个）
- 高优先级资源（如登录界面）先下载
- 支持暂停/恢复下载
- 下载完成后自动开始下一个

### 伪代码实现

```csharp
// 1. 定义下载任务
public class DownloadTask : TaskBase
{
    public string Url { get; private set; }
    public string SavePath { get; private set; }

    public static DownloadTask Create(int serialId, string url, string savePath, int priority)
    {
        DownloadTask task = ReferencePool.Acquire<DownloadTask>();
        task.Initialize(serialId, url, priority, null);
        task.Url = url;
        task.SavePath = savePath;
        return task;
    }

    public override void Clear()
    {
        base.Clear();
        Url = null;
        SavePath = null;
    }
}

// 2. 定义下载代理
public class DownloadAgent : ITaskAgent<DownloadTask>
{
    private UnityWebRequest m_Request;

    public DownloadTask Task { get; private set; }

    public void Initialize() { }

    public StartTaskStatus Start(DownloadTask task)
    {
        Task = task;
        m_Request = UnityWebRequest.Get(task.Url);
        m_Request.SendWebRequest();
        return StartTaskStatus.CanResume; // 需要持续执行
    }

    public void Update(float elapseSeconds, float realElapseSeconds)
    {
        if (m_Request != null && m_Request.isDone)
        {
            // 保存文件...
            File.WriteAllBytes(Task.SavePath, m_Request.downloadHandler.data);
            Task.Done = true; // 标记完成，TaskPool 下一帧会回收
        }
    }

    public void Reset()
    {
        m_Request?.Dispose();
        m_Request = null;
        Task = null;
    }

    public void Shutdown() => Reset();
}

// 3. 初始化任务池（控制并发数为 3）
TaskPool<DownloadTask> downloadPool = new TaskPool<DownloadTask>();
downloadPool.AddAgent(new DownloadAgent());
downloadPool.AddAgent(new DownloadAgent());
downloadPool.AddAgent(new DownloadAgent());

// 4. 提交下载任务（高优先级的先执行）
downloadPool.AddTask(DownloadTask.Create(1, "https://cdn.example.com/login_ui.ab", savePath, priority: 100));
downloadPool.AddTask(DownloadTask.Create(2, "https://cdn.example.com/bgm.ab", savePath, priority: 10));
downloadPool.AddTask(DownloadTask.Create(3, "https://cdn.example.com/level1.ab", savePath, priority: 50));
// ... 提交 200 个任务

// 5. 每帧轮询（在 MonoBehaviour.Update 中调用）
downloadPool.Update(Time.deltaTime, Time.unscaledDeltaTime);
```

**效果**：3 个代理同时工作，每当一个下载完成，自动从等待队列取出下一个最高优先级的任务开始下载，直到所有任务完成。

---

## 五、模块评价

### 优点

1. **设计简洁清晰**：代理模式 + 生产者消费者，代码量少（~450 行），易于理解和维护。
2. **零 GC 设计**：通过 `IReference` + `ReferencePool` 实现对象复用，对 Unity 移动端非常友好。
3. **优先级调度**：内置按优先级排序的等待队列，满足游戏中常见的"重要资源优先加载"需求。
4. **并发控制**：通过代理数量天然限制并发数，简单直观。
5. **暂停/恢复**：一行代码即可暂停整个任务池，适合游戏中切后台等场景。
6. **与 Unity 帧循环无缝集成**：`Update()` 设计天然适配 Unity 的 MonoBehaviour 生命周期。

### 不足

1. **缺乏 async/await 支持**：完全基于轮询（Polling），没有 Task/UniTask 集成，无法使用现代 C# 异步编程模式。
2. **无取消令牌（CancellationToken）**：移除任务的方式比较原始（按 serialId 或 tag），不支持 .NET 标准的取消机制。
3. **无进度回调机制**：缺少内置的进度通知，需要自行在 Agent 中实现。
4. **无错误重试机制**：`UnknownError` 直接丢弃任务，没有重试策略。
5. **无依赖链/DAG 支持**：`HasToWait` 只是简单地保留任务在队列中，无法表达"任务 A 依赖任务 B 完成"这样的关系。
6. **单线程**：所有逻辑在主线程执行，无法利用多核 CPU（当然这在 Unity 中也是常见做法）。

### 是否过时？

**结论：作为学习材料和轻量级场景仍有价值，但在现代项目中推荐使用更先进的方案。**

| 维度 | TaskPool（2013-2021） | 现代方案 |
|---|---|---|
| 异步模型 | 帧轮询 Polling | UniTask / async-await / C# Task |
| 取消机制 | 手动按 ID 移除 | CancellationToken |
| 并发控制 | 代理数量限制 | SemaphoreSlim / Channel |
| 进度报告 | 无内置 | IProgress\<T\> |
| 依赖管理 | 无 | Task.WhenAll / 响应式编程 |
| GC 优化 | ReferencePool | Unity 增量 GC + ValueTask + struct awaiter |
| 多线程 | 无 | Unity Job System / DOTS |

在 2013 年 Unity 4.x 时代，C# 版本低（3.5），没有 async/await，这套基于轮询的设计是当时的**最佳实践**。但到了 2025 年，Unity 已支持 C# 9+、有 UniTask 等成熟异步库、有 Job System 和 DOTS，这套模式在**新项目**中已不是首选。

不过，GameFramework 的 TaskPool 在**理解任务调度原理**方面仍然是极好的教材——它把生产者消费者、优先级队列、对象池、代理模式等概念用最少的代码组合在了一起。

---

## 六、学习路线建议

### 第一阶段：理解基础概念（1-2 周）

- [ ] 彻底读懂本目录下的 6 个文件，手写一遍 TaskPool
- [ ] 学习设计模式：**对象池模式（Object Pool）**、**代理模式（Agent/Worker）**、**生产者-消费者模式**
- [ ] 学习数据结构：**优先级队列**、**链表**、**栈**
- [ ] 推荐书籍：《Head First 设计模式》、《游戏编程模式》（Robert Nystrom）

### 第二阶段：掌握现代 C# 异步（2-4 周）

- [ ] 学习 C# `async/await` 原理，理解状态机编译
- [ ] 学习 `Task`、`ValueTask`、`CancellationToken`、`IProgress<T>`
- [ ] 学习 `SemaphoreSlim`、`Channel<T>` 等并发原语
- [ ] 实践：用 async/await 重写上面的下载管理器示例

### 第三阶段：Unity 异步生态（2-4 周）

- [ ] 学习 **UniTask**（https://github.com/Cysharp/UniTask）——Unity 最流行的零 GC 异步库
- [ ] 学习 Unity **Addressables** 的异步加载模型（AsyncOperationHandle）
- [ ] 学习 Unity **Job System** + **Burst Compiler** 基础（面向 CPU 密集型任务）
- [ ] 实践：用 UniTask + SemaphoreSlim 实现一个并发可控的资源加载器

### 第四阶段：进阶架构（持续学习）

- [ ] 学习 **响应式编程（Rx/UniRx）**，理解事件流的任务组合
- [ ] 学习 **ECS/DOTS** 中的任务调度思想
- [ ] 了解服务端任务调度：消息队列（RabbitMQ/Kafka）、分布式任务（Celery/Hangfire）
- [ ] 阅读 GameFramework 的 `DownloadManager`、`ResourceManager` 源码，看 TaskPool 在框架中的实际应用

### 推荐学习资源

| 资源 | 说明 |
|---|---|
| [UniTask GitHub](https://github.com/Cysharp/UniTask) | Unity 零 GC 异步库，附带大量文档和示例 |
| [Game Programming Patterns](https://gameprogrammingpatterns.com/) | 免费在线阅读，包含对象池等游戏相关模式 |
| [GameFramework 官网](https://gameframework.cn/) | 框架完整文档和教程 |
| [C# in Depth](https://csharpindepth.com/) | 深入理解 C# 异步机制 |
| [Unity DOTS 官方文档](https://docs.unity3d.com/Packages/com.unity.entities@latest) | Job System 和 ECS 学习 |

---

> **总结**：TaskPool 是一个经典的轻量级任务调度模块，麻雀虽小五脏俱全。它在 GameFramework 中主要服务于资源下载、资源加载等需要并发控制的场景。虽然在现代 Unity 开发中有更先进的替代方案，但其设计思想——**有限工人 + 优先级队列 + 对象复用**——是任务调度领域的通用范式，值得深入理解。
