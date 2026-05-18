# MemoryPool 模块深度解析

> 文档生成时间：2026-05-18  
> 模块路径：`CustomPackages/OhMyPackage/Runtime/Core/Framework/Base/MemoryPool`

---

## 一、模块概览

该模块实现了一套**通用 C# 对象内存池（Object Pool）**，专为 Unity 游戏运行时设计。其核心目标是**复用已分配的托管堆对象，减少 GC（垃圾回收）频率**，从而提升游戏帧率稳定性。

### 文件职责速览

| 文件 | 类型 | 职责 |
|------|------|------|
| `IMemory.cs` | Interface | 所有可入池对象必须实现的接口，仅含 `Clear()` 一个方法 |
| `MemoryPool.cs` | static partial class | 核心入口，持有全局 `Dictionary<Type, MemoryCollection>`，提供 `Acquire` / `Release` / `Add` / `Remove` API |
| `MemoryPool.MemoryCollection.cs` | 内部类 | 单类型的对象桶，底层用 `Queue<IMemory>` 存储，包含完整统计字段 |
| `MemoryPoolExtension.cs` | 扩展类 + 抽象基类 | 提供 `MemoryObject` 抽象基类（含 `InitFromPool` / `RecycleToPool` 生命周期钩子）和 `Alloc` / `Dealloc` 便捷 API |
| `MemoryPoolInfo.cs` | 只读结构体 | 快照式监控数据（未使用数、使用中数、累计申请/归还/新增/移除数） |
| `MemoryPoolSetting.cs` | MonoBehaviour | Unity Inspector 配置入口，支持 4 级严格检查策略，`Start()` 后自销毁 |

---

## 二、设计结构图

```
MemoryPool（静态门面）
│
├── Dictionary<Type, MemoryCollection>   ← 按类型隔离，线程安全（lock）
│         │
│         └── Queue<IMemory>             ← FIFO 复用队列
│
├── IMemory                              ← 最小入池契约（只要实现 Clear()）
│
├── MemoryObject                         ← 可选抽象基类（提供钩子方法）
│       ├── InitFromPool()               ← Alloc 时自动调用
│       └── RecycleToPool()             ← Dealloc 时自动调用
│
└── MemoryPoolSetting (MonoBehaviour)    ← 运行时配置（严格检查模式）
```

### 核心流程

```
Acquire<T>()
  ├─ 队列有对象  →  Dequeue() 直接复用（零 GC）
  └─ 队列为空   →  new T() 分配新对象

Release(memory)
  ├─ 调用 memory.Clear()  ←  重置对象状态
  └─ Enqueue() 归还队列
```

### 严格检查机制

`MemoryStrictCheckType` 提供 4 档配置：

| 枚举值 | 生效时机 | 用途 |
|--------|---------|------|
| `AlwaysEnable` | 始终开启 | 线上排查特殊问题 |
| `OnlyEnableWhenDevelopment` | Debug Build | **推荐默认值**，开发期检测双重释放 |
| `OnlyEnableInEditor` | 仅 Editor | 快速迭代时最低开销 |
| `AlwaysDisable` | 始终关闭 | 极限性能场景 |

开启后会检测**重复 Release 同一对象**（`_memories.Contains(memory)` 防重入），代价是 `O(n)` 遍历，故生产包默认关闭。

---

## 三、行业常见用法：子弹系统

射击游戏中子弹每帧可能创建/销毁数十个，是内存池最经典的应用场景。

### 第一步：实现 `IMemory`（推荐继承 `MemoryObject`）

```csharp
using OhMyPackage;
using UnityEngine;

public class BulletData : MemoryObject
{
    public Vector3 StartPosition;
    public Vector3 Direction;
    public float Speed;
    public float Damage;
    public float LifeTime;

    // Alloc 时自动调用 —— 相当于"构造函数"
    public override void InitFromPool()
    {
        Speed    = 20f;
        Damage   = 10f;
        LifeTime = 3f;
    }

    // Dealloc 时自动调用 —— 相当于"析构函数"
    public override void RecycleToPool()
    {
        // 可在此解除事件监听、清理引用等
    }

    // Clear() 由 Release 自动调用，重置数据防止脏读
    public override void Clear()
    {
        StartPosition = Vector3.zero;
        Direction     = Vector3.zero;
        Speed         = 0f;
        Damage        = 0f;
        LifeTime      = 0f;
    }
}
```

### 第二步：发射子弹（取出对象）

```csharp
void Fire(Vector3 muzzlePos, Vector3 dir)
{
    // 从池中取出，内部自动调用 InitFromPool()
    BulletData bullet = MemoryPool.Alloc<BulletData>();
    bullet.StartPosition = muzzlePos;
    bullet.Direction     = dir;

    // 交给飞行系统处理...
    BulletSystem.Instance.Register(bullet);
}
```

### 第三步：子弹销毁（归还对象）

```csharp
void OnBulletHitOrExpire(BulletData bullet)
{
    // 自动调用 RecycleToPool() + Clear() 再归还队列
    MemoryPool.Dealloc(bullet);
}
```

### 第四步：预热（可选，关卡开始前提前分配）

```csharp
void OnLevelStart()
{
    // 预先向池中填入 50 个子弹对象，避免战斗开始瞬间的 GC 抖动
    MemoryPool.Add<BulletData>(50);
}
```

### 第五步：监控（Editor 调试面板）

```csharp
void OnGUI()
{
    foreach (var info in MemoryPool.GetAllMemoryPoolInfos())
    {
        GUILayout.Label($"[{info.Type.Name}] " +
            $"空闲:{info.UnusedMemoryCount} " +
            $"使用中:{info.UsingMemoryCount} " +
            $"累计申请:{info.AcquireMemoryCount}");
    }
}
```

---

## 四、模块评价

### 优点

| 维度 | 评价 |
|------|------|
| **设计简洁** | API 极简（`Acquire`/`Release` 两个核心方法），学习成本接近零 |
| **类型安全** | 泛型 + 编译期约束 `where T : class, IMemory, new()`，无强转风险 |
| **线程安全** | 队列操作均有 `lock`，可在子线程使用 |
| **可观测性** | 6 项统计指标（未使用/使用中/申请/归还/新增/移除）满足性能调优需求 |
| **双层 API** | 低层 `Acquire/Release` + 高层 `Alloc/Dealloc`（含生命周期钩子），灵活覆盖不同场景 |
| **Unity 集成** | `MemoryPoolSetting` 挂载即用，Inspector 可视化配置严格模式 |

### 不足与局限

| 问题 | 说明 |
|------|------|
| **无容量上限** | 池无 `maxSize` 配置，极端情况下可能无限膨胀，占用过多内存 |
| **严格检查开销大** | `Contains()` 为 O(n) 遍历，对象数量大时性能下降明显 |
| **无自动收缩** | 没有基于 LRU 或时间的自动 trim 策略，关卡切换后需手动 `RemoveAll` |
| **仅支持 class** | 值类型（struct）无法入池，不适用于 `NativeArray` 等非托管场景 |
| **无异步预热** | `Add()` 是同步批量创建，可能在主线程造成短暂卡顿 |

### 是否过时？

**结论：不过时，但需要按场景选型。**

该模块所解决的核心问题——**减少 GC、复用对象**——在 Unity 开发中截至 2026 年依然是刚需：

- Unity 的 **Incremental GC** 虽已改善，但在 IL2CPP 打包后 GC.Alloc 峰值仍会造成 Spike
- `.NET 6+` 内置 [`System.Buffers.ObjectPool<T>`](https://docs.microsoft.com/en-us/dotnet/api/system.buffers.objectpool-1) 和 `Microsoft.Extensions.ObjectPool`，但在 Unity 生态中引入成本高
- Unity 2021+ 引入的 **Unity Object Pool**（`UnityEngine.Pool.ObjectPool<T>`）是官方解，但仅适合 `GameObject`/`Component` 类对象

本模块的适用场景与官方方案的对比：

| 场景 | 本模块 | `UnityEngine.Pool.ObjectPool<T>` |
|------|--------|----------------------------------|
| 纯 C# 数据对象（事件、消息、战斗数据） | ✅ 首选 | ❌ 不适用 |
| GameObject / Particle / Prefab 实例 | ⚠️ 可用但需手动管理 | ✅ 官方首选 |
| 高频网络消息包 | ✅ 优秀 | ❌ 不适用 |
| 需要 Span/NativeArray 的 ECS 数据 | ❌ 不支持 | ❌ 不支持（需 NativePool） |

---

## 五、学习路线建议

### 基础阶段（理解"为什么需要池"）

1. **理解 .NET GC 原理**
   - 掌握分代回收（Gen0/Gen1/Gen2）、Finalizer 队列、LOH 大对象堆
   - 推荐资源：《CLR via C#》第 21 章、Microsoft Docs `.NET GC Overview`

2. **Unity Profiler 实战**
   - 用 Profiler 的 `Memory` 模块识别 GC.Alloc 热点
   - 重点关注 `GC.Alloc` 列和 `GC Reserved` 趋势

3. **阅读本模块源码**
   - 逐行读懂 `MemoryCollection`，理解 Queue 入队/出队和 lock 的必要性

### 进阶阶段（掌握更多池化技术）

4. **对比学习 Unity 官方对象池**
   ```
   UnityEngine.Pool.ObjectPool<T>
   UnityEngine.Pool.CollectionPool<TCollection, TItem>
   UnityEngine.Pool.ListPool<T> / DictionaryPool<K,V>
   ```

5. **`System.Buffers.ArrayPool<T>`**
   - .NET 内置数组池，适合高频临时数组申请（如序列化 Buffer）
   - 理解 `Rent()` / `Return()` 和 `MemoryPool<T>` 的区别

6. **`Microsoft.Extensions.ObjectPool`**
   - 支持 `IPooledObjectPolicy<T>`，可自定义创建/归还策略
   - 适合服务端 Unity（如 Headless Server）

### 高阶阶段（性能工程与无 GC 架构）

7. **Unity ECS / DOTS**
   - `NativeArray<T>`、`BlobArray`、`UnsafeList<T>` —— 彻底绕开托管 GC
   - Burst Compiler + Job System 的内存模型

8. **值类型优化**
   - `readonly struct`、`in` 参数传递、`Span<T>` 零拷贝
   - `stackalloc` 在高频路径中的应用

9. **性能测试方法论**
   - BenchmarkDotNet 对比有池/无池的分配开销
   - Unity `FrameTimingManager` + `ProfilerRecorder` API 做精细 Profiling

### 推荐书单

| 书名 | 重点章节 |
|------|---------|
| 《CLR via C#》（第4版） | 第20章（异常）、第21章（GC）|
| 《Game Programming Patterns》 | Object Pool 章节（有免费网页版）|
| 《Pro .NET Performance》 | 第3章（内存管理）|
| Unity 官方文档 | [UnityEngine.Pool 命名空间](https://docs.unity3d.com/ScriptReference/Pool.ObjectPool_1.html) |

---

*本文档由 GitHub Copilot 自动生成，基于源码静态分析，如有出入以实际代码为准。*
