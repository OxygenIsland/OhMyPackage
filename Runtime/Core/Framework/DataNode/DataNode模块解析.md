# DataNode 模块解析

> 源码路径：`Assets/Scripts/GameFramework/DataNode/`
> 框架版本：GameFramework © 2013–2021 Jiang Yin

---

## 一、模块文件一览

| 文件 | 类/接口名 | 职责 |
|---|---|---|
| `IDataNode.cs` | `IDataNode` | 单个数据节点的公共接口：读写数据、管理子节点 |
| `IDataNodeManager.cs` | `IDataNodeManager` | 管理器公共接口：路径寻址的增删查改 |
| `DataNodeManager.cs` | `DataNodeManager`（partial） | 管理器实现：路径解析、节点的创建与删除 |
| `DataNodeManager.DataNode.cs` | `DataNodeManager.DataNode`（partial 内嵌类） | 节点实体：存储父子关系、持有 Variable 数据、对象池管理 |

> 采用 **partial class** 将管理器与其内嵌节点类拆分到两个文件，职责清晰，可独立阅读。

---

## 二、模块作用

### 核心定位：路径寻址的运行时数据树

DataNode 模块实现了一棵**全局共享的树形数据结构（Data Tree / Blackboard Tree）**，用于在游戏运行时以**路径字符串**存取任意类型的数据。

```
<Root>（根节点，不可见）
├── Player
│   ├── Name          → VarString("英雄")
│   ├── Level         → VarInt(15)
│   └── Attributes
│       ├── HP        → VarInt(350)
│       ├── MaxHP     → VarInt(500)
│       └── Speed     → VarFloat(6.5f)
├── Game
│   ├── Stage         → VarInt(3)
│   ├── IsPaused      → VarBool(false)
│   └── ElapsedTime   → VarFloat(128.4f)
└── Config
    └── MusicVolume   → VarFloat(0.8f)
```

路径分隔符支持 `.`、`/`、`\` 三种写法，效果完全等价：

```csharp
dataNodeManager.GetData<VarInt>("Player.Attributes.HP");
dataNodeManager.GetData<VarInt>("Player/Attributes/HP");
```

### 三大能力

| 能力 | 说明 |
|---|---|
| **路径自动创建** | `SetData` 时若路径不存在，沿途自动创建中间节点（`GetOrAddNode`）|
| **强类型存取** | 数据存为 `Variable`（Variable 模块），`GetData<VarInt>()` 直接返回强类型 |
| **全链路零 GC** | `DataNode` 和 `Variable` 均实现 `IReference`，全部由 `ReferencePool` 管理 |

### 节点数据的完整生命周期

```
写入：ReferencePool.Acquire<VarInt>() → 赋值 → SetData() → 节点持有
覆写：SetData() 检测到旧值 → ReferencePool.Release(旧 Variable) → 持有新值
删除：RemoveChild() / RemoveNode() → ReferencePool.Release(DataNode)
         → DataNode.Clear() → ReferencePool.Release(Variable)
关闭：DataNodeManager.Shutdown() → ReferencePool.Release(m_Root)（递归清理全树）
```

---

## 三、关键实现细节

### 路径解析

```csharp
// DataNodeManager.cs
private static readonly string[] PathSplitSeparator = new string[] { ".", "/", "\\" };

private static string[] GetSplitedPath(string path)
    => path.Split(PathSplitSeparator, StringSplitOptions.RemoveEmptyEntries);
```

`GetNode` 沿分段逐层向下查找，找不到返回 `null`；`GetOrAddNode` 找不到则自动补建节点。

### SetData 自动归还旧值

```csharp
// DataNodeManager.DataNode.cs
public void SetData(Variable data)
{
    if (m_Data != null)
    {
        ReferencePool.Release(m_Data); // 旧 Variable 自动归还对象池
    }
    m_Data = data;
}
```

调用方**不需要手动 Release 旧值**，防止内存泄漏。

### 子节点查找：线性扫描

```csharp
// 内部使用 List<DataNode>，按名称线性遍历
foreach (DataNode child in m_Childs)
{
    if (child.Name == name) return child;
}
```

> ⚠️ **性能注意**：每层子节点查找时间复杂度为 O(n)。同级节点数量小（< 20）时无感知；若同级有大量节点（如存储 100 个 Item 数据），应考虑用 `Dictionary` 代替或改用其他方案。

---

## 四、行业常见使用示例

### 场景：RPG 游戏的存档系统 + 跨系统共享状态

游戏需要在战斗系统、UI 系统、存档系统之间共享玩家状态，并支持存档时一次性读取全部数据。

#### 步骤一：游戏启动时初始化数据树（加载存档）

```csharp
// GameStartProcedure.cs
void InitPlayerData(SaveData save)
{
    // 玩家基础属性
    var name = ReferencePool.Acquire<VarString>();
    name.Value = save.playerName;
    dataNodeManager.SetData("Player.Name", name);

    var hp = ReferencePool.Acquire<VarInt>();
    hp.Value = save.hp;
    dataNodeManager.SetData("Player.Attributes.HP", hp);

    var gold = ReferencePool.Acquire<VarInt>();
    gold.Value = save.gold;
    dataNodeManager.SetData("Player.Inventory.Gold", gold);

    // 游戏进度
    var stage = ReferencePool.Acquire<VarInt>();
    stage.Value = save.currentStage;
    dataNodeManager.SetData("Game.Stage", stage);
}
```

#### 步骤二：战斗系统写入数据

```csharp
// BattleSystem.cs — 受伤时更新 HP
void TakeDamage(int damage)
{
    int current = dataNodeManager.GetData<VarInt>("Player.Attributes.HP").Value;
    int newHp = Mathf.Max(0, current - damage);

    // 覆写：框架自动 Release 旧 VarInt，持有新的
    var v = ReferencePool.Acquire<VarInt>();
    v.Value = newHp;
    dataNodeManager.SetData("Player.Attributes.HP", v);

    // 配合事件系统通知 UI（黑板本身不推送通知）
    EventSystem.Fire(HPChangedEventArgs.Create(newHp));
}
```

#### 步骤三：存档系统一次性读取全树

```csharp
// SaveSystem.cs — 存档时遍历树形结构
void Save()
{
    var saveData = new SaveData
    {
        playerName = dataNodeManager.GetData<VarString>("Player.Name").Value,
        hp         = dataNodeManager.GetData<VarInt>("Player.Attributes.HP").Value,
        gold       = dataNodeManager.GetData<VarInt>("Player.Inventory.Gold").Value,
        stage      = dataNodeManager.GetData<VarInt>("Game.Stage").Value,
    };
    File.WriteAllText(savePath, JsonUtility.ToJson(saveData));
}
```

#### 步骤四：场景切换时清理局部数据

```csharp
// 只清理战斗相关数据，保留玩家基础属性
dataNodeManager.RemoveNode("Game.Battle");

// 或切换到新关卡时重置整棵树
dataNodeManager.Clear();
```

---

## 五、模块评价

### 优点

| 方面 | 说明 |
|---|---|
| **全局统一访问点** | 任意系统持有 `IDataNodeManager` 引用即可读写，无需互相依赖 |
| **层级命名空间** | 树形路径天然避免键名冲突，比 `Dict<string, object>` 更具结构化语义 |
| **零 GC 全链路** | 节点和数据均对象池化，`SetData` 覆写自动回收旧值，高频写入无 GC |
| **按需创建中间节点** | 写入深路径无需提前建树，`GetOrAddNode` 自动补建 |
| **接口隔离清晰** | `IDataNode` / `IDataNodeManager` 分离，单元测试友好 |
| **存档天然友好** | 树形结构可直接遍历序列化，逻辑层与序列化层无缝衔接 |

### 局限性

| 方面 | 说明 |
|---|---|
| **子节点线性查找** | `List<DataNode>` + `foreach`，同级节点多时性能下降（O(n) 每层）|
| **无变更通知** | 写入数据后不推送任何事件，驱动 UI 更新需额外接入事件系统 |
| **路径为魔法字符串** | `"Player.Attributes.HP"` 编译期不检查，拼写错误只在运行时暴露 |
| **不适合响应式数据绑定** | 与 R3/UniRx 无原生集成，数据绑定场景代码冗余 |
| **无类型约束保护** | 同一路径写入不同类型不会报编译错误，运行时 `(T)m_Data` 可能抛异常 |

### 是否过时？

**结论：核心设计仍有价值，但在新项目中通常只作为配置/存档的中间层，而非首选的响应式状态容器。**

| 场景 | DataNode | 现代替代 / 搭配 |
|---|---|---|
| 游戏存档的运行时缓存层 | ✅ 适用 | — |
| 跨系统配置数据共享（只读为主）| ✅ 适用 | `ScriptableObject` 更直观（编辑器可见）|
| UI 数据绑定（HP 变化驱动血条）| ❌ 不适用 | **R3 `ReactiveProperty<T>`**（本项目已集成）|
| AI 行为树黑板（每个 Agent 独立）| ⚠️ 可用，但偏重 | NodeCanvas / BehaviorDesigner 内置黑板更专业 |
| 高性能 ECS 状态（大量实体）| ❌ 不适用 | Unity DOTS / ECS |
| 运行时动态 JSON 配置解析 | ✅ 适用 | `Newtonsoft.Json` 直接反序列化到强类型 Model 更安全 |

> **在本项目中**：新增的响应式状态应优先使用 **R3 `ReactiveProperty<T>`**（已通过 VContainer 注入）；DataNode 最适合继续承担**存档数据暂存**和**游戏进度全局快照**的角色。

---

## 六、学习路线建议

### 基础阶段（理解设计意图）

1. **先掌握 Variable 模块**
   路径：`GameFramework/Base/Variable/`（可参考同目录 `Variable模块解析.md`）
   理解：DataNode 的每个节点持有一个 `Variable`，不懂 Variable 就读不懂 DataNode 的数据操作。

2. **理解 ReferencePool（对象池）**
   路径：`GameFramework/Base/ReferencePool/`
   理解：DataNode 和 Variable 都实现 `IReference`，对象的生命周期由池管理，而不是 GC。

3. **手写一个最小树形结构**
   用普通 C# 实现一个 `Dictionary<string, object>` 的嵌套版本，再对比 DataNode 的设计，理解路径寻址的价值。

### 进阶阶段（横向对比）

4. **对比 Unity Animator 参数系统**
   `animator.SetFloat("Speed", 5f)` 本质上是一个用 `int` 哈希寻址的黑板，对比 DataNode 的字符串路径寻址，理解两者性能取舍。

5. **学习 Composite 设计模式**
   《Design Patterns》GoF 第 163 页，或《Game Programming Patterns》在线版。
   DataNode 是 Composite 模式的教科书级实现：节点既可以是叶节点（有数据无子节点），也可以是容器节点（无数据有子节点），或两者兼有。

6. **对比 `ScriptableObject` 变量架构**
   参考 Ryan Hipple 的 [Unite Austin 2017 演讲](https://www.youtube.com/watch?v=raQ3iHhE_Kk)
   理解：ScriptableObject 变量在 Inspector 中可视化、可被 Designer 直接配置；DataNode 在运行时动态创建、适合代码驱动的存档场景。

### 现代化阶段（结合本项目技术栈）

7. **学习 R3 响应式编程（本项目已集成）**
   关键类型：`ReactiveProperty<T>`、`ReadOnlyReactiveProperty<T>`、`Subject<T>`
   
   理解何时选哪个：
   ```
   DataNode          → 存档/配置快照（批量读取，无需订阅）
   ReactiveProperty  → 运行时状态（需要 UI 或其他系统实时响应变化）
   ```

8. **实践：用两种方式实现同一个需求**

   **需求**：玩家金币变化时，HUD 金币数字和商店界面金币数字同步刷新。

   - **方式 A（DataNode + Event）**：
     ```csharp
     // 写入
     var v = ReferencePool.Acquire<VarInt>();
     v.Value = newGold;
     dataNodeManager.SetData("Player.Inventory.Gold", v);
     eventSystem.Fire(GoldChangedEventArgs.Create(newGold)); // 手动触发
     
     // 订阅（HUD）
     eventSystem.Subscribe(GoldChangedEventArgs.EventId, OnGoldChanged);
     ```
   
   - **方式 B（ReactiveProperty）**：
     ```csharp
     // 定义（PlayerModel）
     public readonly ReactiveProperty<int> Gold = new(0);
     
     // 写入（任意系统）
     playerModel.Gold.Value = newGold; // 自动推送
     
     // 订阅（HUD，自动取消订阅）
     playerModel.Gold.Subscribe(g => goldText.text = g.ToString()).AddTo(this);
     ```

   对比代码量和维护成本，建立自己的技术选型判断力。

9. **进阶：学习 Unity DOTS / ECS（可选）**
   若游戏有大量同类实体的共享状态（如 MOBA 里 100 个小兵的 HP），ECS 的组件数组是 DataNode 树形结构无法比拟的性能方案。
   推荐从 Unity 官方 DOTS 教程入门，理解 SoA（结构体数组）思想。

### 推荐资源

| 资源 | 说明 |
|---|---|
| [GameFramework 官网](https://gameframework.cn/) | 框架原作者 Jiang Yin 的文档与 Demo |
| 《Game Programming Patterns》Robert Nystrom | Composite、Object Pool、Blackboard 模式权威讲解（[免费在线版](https://gameprogrammingpatterns.com/)）|
| [R3 GitHub](https://github.com/Cysharp/R3) | 本项目响应式框架，替代 UniRx，支持 Unity 6 |
| Ryan Hipple - Unite Austin 2017 | ScriptableObject 架构，与 DataNode 的横向对比参考 |
| Unity 官方 DOTS 文档 | ECS 高性能状态管理，了解 DataNode 的性能边界在哪里 |
