# EventPool 模块解析

---

## 一、模块结构与职责

该模块共 4 个文件，构成了一个**泛型事件池（Event Pool）**系统，属于经典的**观察者模式（Observer Pattern）/ 发布-订阅模式（Pub-Sub）**实现。来源于国内知名的开源 Unity 游戏框架 **GameFramework**（作者：EllanJiang）。

| 文件 | 职责 |
|---|---|
| BaseEventArgs.cs | 事件参数基类，要求所有事件定义一个 `Id`（整型编号）用于区分事件类型 |
| EventPoolMode.cs | 枚举（`[Flags]`），控制事件池行为策略：是否允许无处理器、多处理器、重复处理器 |
| EventPool.Event.cs | 内部类 `Event`，封装一次事件的发送者 + 事件参数，实现 `IReference` 接口以配合**引用池**复用对象（零 GC） |
| EventPool.cs | **核心类**，管理事件的订阅（`Subscribe`）、取消订阅（`Unsubscribe`）、抛出（`Fire`/`FireNow`）和轮询分发（`Update`） |

## 二、核心设计要点

### 1. 延迟分发 vs 立即分发

- `Fire()`：线程安全，将事件入队（`Queue<Event>`），在下一帧 `Update()` 时统一分发。适合多线程场景（如网络线程抛事件到主线程）。
- `FireNow()`：立即分发，不经过队列，非线程安全。适合主线程内需要即时响应的场景。

### 2. 零 GC 设计

`Event` 内部类实现了 `IReference`，通过框架的 `ReferencePool` 进行对象池复用，避免频繁 `new` 产生 GC。

### 3. 安全遍历

`m_CachedNodes` / `m_TempNodes` 解决了**遍历过程中取消订阅**导致链表断裂的经典问题。

### 4. 模式控制（EventPoolMode）

- `Default`（0）：严格模式——同一事件必须有且仅有一个处理函数
- `AllowNoHandler`：允许没有监听者（不抛异常）
- `AllowMultiHandler`：允许多个监听者
- `AllowDuplicateHandler`：允许同一个委托重复注册

---

## 三、行业常见使用示例：RPG 游戏中的背包系统

假设你正在做一个 RPG 游戏，玩家获得道具后需要通知 UI 刷新、成就系统检测、音效播放等多个模块：

```csharp
// 1. 定义事件参数
public class ItemObtainedEventArgs : BaseEventArgs
{
    public static readonly int EventId = typeof(ItemObtainedEventArgs).GetHashCode();
    
    public override int Id => EventId;
    
    public int ItemId { get; private set; }
    public int Count { get; private set; }

    // 配合引用池的 Create/Clear 模式
    public static ItemObtainedEventArgs Create(int itemId, int count)
    {
        ItemObtainedEventArgs e = ReferencePool.Acquire<ItemObtainedEventArgs>();
        e.ItemId = itemId;
        e.Count = count;
        return e;
    }

    public override void Clear()
    {
        ItemId = 0;
        Count = 0;
    }
}

// 2. UI 背包界面 —— 订阅事件
public class BagUI : MonoBehaviour
{
    void OnEnable()
    {
        GameEntry.Event.Subscribe(ItemObtainedEventArgs.EventId, OnItemObtained);
    }

    void OnDisable()
    {
        GameEntry.Event.Unsubscribe(ItemObtainedEventArgs.EventId, OnItemObtained);
    }

    private void OnItemObtained(object sender, BaseEventArgs e)
    {
        ItemObtainedEventArgs args = (ItemObtainedEventArgs)e;
        Debug.Log($"UI 刷新：获得道具 {args.ItemId} x{args.Count}");
        RefreshBagView();
    }
}

// 3. 拾取逻辑 —— 抛出事件
public class PickupItem : MonoBehaviour
{
    public void OnPlayerPickup(int itemId, int count)
    {
        // 通知所有订阅者（UI、成就、音效...），彼此完全解耦
        GameEntry.Event.Fire(this, ItemObtainedEventArgs.Create(itemId, count));
    }
}
```

**好处**：拾取逻辑完全不知道 UI、成就、音效的存在，各模块只关心自己感兴趣的事件，实现了**高内聚、低耦合**。

---

## 四、模块评价

### 优点

- **成熟稳定**：GameFramework 在国内 Unity 社区使用广泛，经过大量商业项目验证
- **零 GC**：引用池 + 对象池设计对移动端非常友好
- **线程安全的 Fire**：适合网络消息回调等多线程场景
- **遍历安全**：正确处理了遍历中取消订阅的边界情况
- **模式可配**：`EventPoolMode` 枚举提供了灵活的行为控制

### 不足 / 局限

| 方面 | 说明 |
|---|---|
| **类型安全弱** | 事件通过 `int Id` 区分，订阅/抛出时靠开发者自觉匹配类型，编译器无法检查 |
| **手动管理生命周期** | 需要手动 Subscribe/Unsubscribe，忘记取消订阅会导致内存泄漏或空引用 |
| **不支持异步/await** | 整个设计基于同步回调，不适配现代 C# 的 `async/await` 模式 |
| **无优先级/拦截机制** | 无法控制事件处理顺序，也没有"消费"事件的机制 |

### 是否过时？

**没有过时，但有更现代的替代方案。**

这种事件池模式至今仍是 Unity 游戏开发的**主流做法之一**，尤其在需要精细控制 GC 和性能的手游项目中。但行业也在演进：

| 方案 | 特点 |
|---|---|
| **UniRx / R3** | 响应式编程，支持流操作（Filter、Throttle、Merge 等），更声明式 |
| **MessagePipe** | 由 UniTask 作者 neuecc 开发，支持 DI、异步、发布-订阅，更现代 |
| **Unity DOTS EventSystem** | ECS 架构下的事件方案，面向高性能场景 |
| **ScriptableObject Event** | 轻量级，基于 Unity 资产系统，适合小型项目 |

对于**中大型商业项目**，GameFramework 的 EventPool 依然是可靠的选择；对于**新项目**，可以考虑更现代的方案（如 MessagePipe + UniTask）。

---

## 五、学习路线建议

```
阶段一：基础扎实
├── C# 委托、事件、泛型、lambda 表达式
├── 设计模式：观察者模式、发布-订阅模式、对象池模式
└── Unity MonoBehaviour 生命周期

阶段二：深入理解 GameFramework
├── ReferencePool（引用池）—— 理解零 GC 的核心
├── EventPool（本模块）—— 理解事件驱动架构
├── Module/Manager 模式 —— 理解框架的模块管理
└── 从 GameFramework 官方 StarForce 示例项目入手实践

阶段三：现代化演进
├── UniTask —— Unity 下的异步编程
├── R3（UniRx 后继者）—— 响应式编程
├── MessagePipe —— 现代发布-订阅 + 依赖注入
└── VContainer / Zenject —— IoC 容器，理解依赖注入

阶段四：架构视野
├── ECS（Unity DOTS）—— 数据驱动架构
├── MVC / MVP / MVVM 在游戏中的实践
├── 阅读 ET Framework、QFramework 等其他开源框架对比学习
└── 关注 GDC 分享和 Unity 官方技术博客
```

**核心建议**：先把 GameFramework 的 StarForce 示例项目完整跑通并理解，这是最好的入门路径。然后带着问题（比如"为什么要用 int Id 而不是泛型类型做 key？"）去对比学习其他框架，就能快速建立架构判断力。
