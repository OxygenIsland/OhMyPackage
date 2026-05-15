# Variable 模块解析

> 源码路径：`Assets/Scripts/GameFramework/Base/Variable/`
> 框架版本：GameFramework © 2013–2021 Jiang Yin

---

## 一、模块文件一览

| 文件 | 类名 | 说明 |
|---|---|---|
| `Variable.cs` | `Variable` | 抽象基类，类型擦除接口，实现 `IReference` |
| `GenericVariable.cs` | `Variable<T>` | 泛型抽象子类，持有强类型值 `T` |

---

## 二、模块作用

### 核心思想

Variable 模块提供了一套**强类型值包装器（Value Wrapper）**，目的是让任意类型的数据可以：

1. **以统一的基类接口传递**（`Variable`，类似类型擦除），外部代码无需知道具体类型。
2. **以强类型方式读写**（`Variable<T>.Value`），避免装箱/拆箱带来的错误。
3. **与对象池（ReferencePool）集成**（实现 `IReference`），变量对象可被池化复用，减少 GC 压力。

### 类层次结构

```
IReference（接口）
  └── Variable（抽象基类，类型擦除）
        └── Variable<T>（泛型抽象子类，强类型）
              ├── VarInt : Variable<int>        ← 用户自定义具体类
              ├── VarFloat : Variable<float>
              ├── VarString : Variable<string>
              ├── VarBool : Variable<bool>
              └── VarObject : Variable<object>
              ... 等
```

> **注意**：框架本身只提供抽象层，`VarInt`、`VarFloat` 等具体类需要开发者在项目中自行派生（通常只需一行代码）。

### 关键设计点

- `Variable`（无泛型）：用于**不需要关心具体类型**的场合，如 DataNode 数据节点存储、事件参数包装。提供 `object GetValue()` / `void SetValue(object)` 供反射或通用场景使用。
- `Variable<T>`（有泛型）：提供 `T Value { get; set; }` 属性，**零装箱访问**值类型。`Clear()` 将值重置为 `default(T)`，配合 ReferencePool 归还时清理状态。
- **IReference**：标记该对象受 `ReferencePool` 管理，`Acquire<T>()` 取出，`Release()` 归还，杜绝频繁 new/GC。

---

## 三、在项目中的实际用途

本项目中，Variable 最核心的应用在于 **DataNode 模块**（`DataNodeManager.DataNode.cs`）：

```csharp
// DataNode 内部持有 Variable（基类，不关心类型）
private Variable m_Data;
```

DataNode 构成一棵**黑板树（Blackboard Tree）**，路径如 `"Player.Attributes.HP"`，每个节点存一个 Variable。这让游戏全局共享状态可以按树形路径访问，类似于 Unity Animator 里的参数黑板。

---

## 四、行业常见使用示例

### 场景：玩家属性黑板系统

游戏中需要在不同系统间共享玩家数据（血量、金币、等级），且类型各不相同。

#### 步骤一：派生具体 Variable 类型

```csharp
// 通常只需一行，框架惯例放在 VariableExtension 文件中
public sealed class VarInt    : Variable<int>    { }
public sealed class VarFloat  : Variable<float>  { }
public sealed class VarString : Variable<string> { }
```

#### 步骤二：写入数据（通过 DataNode 黑板）

```csharp
// 从对象池取出 VarInt，赋值后写入黑板
VarInt hpVar = ReferencePool.Acquire<VarInt>();
hpVar.Value = 100;
dataNodeManager.SetData("Player.Attributes.HP", hpVar);

VarString nameVar = ReferencePool.Acquire<VarString>();
nameVar.Value = "战士";
dataNodeManager.SetData("Player.Name", nameVar);
```

#### 步骤三：读取数据

```csharp
// 读取时知道类型，直接强类型访问，无装箱
int hp = dataNodeManager.GetData<VarInt>("Player.Attributes.HP").Value;

// 或通过类型擦除的基类访问（动态场景，如存档序列化）
Variable raw = dataNodeManager.GetData("Player.Attributes.HP");
object boxedHp = raw.GetValue(); // 返回 object
```

#### 步骤四：归还到对象池

```csharp
// 清理节点数据时，Variable 也随之归还，供下次复用
dataNodeManager.RemoveData("Player.Attributes.HP"); 
// DataNode 内部会调用 ReferencePool.Release(m_Data)，触发 Clear()
```

---

## 五、模块评价

### 优点

| 方面 | 说明 |
|---|---|
| **零 GC 设计** | 配合 ReferencePool，变量对象可复用，高频写入场景（如每帧更新的 AI 黑板）无 GC 开销 |
| **统一接口** | `Variable` 基类使 DataNode、事件系统等无需关心具体类型，松耦合 |
| **类型安全** | 强类型 `Value` 属性，编译期检查，优于 `Dictionary<string, object>` |
| **扩展性强** | 新增类型只需一行派生代码，对框架无侵入 |

### 局限性

| 方面 | 说明 |
|---|---|
| **需要手动派生** | 每种值类型都要派生一个具体类，有少量样板代码 |
| **无响应式能力** | 值变更不会自动通知订阅者，需配合事件系统手动触发 |
| **无法替代响应式属性** | 若需要数据绑定（如 UI 自动刷新），需额外写事件分发逻辑 |

### 是否过时？

**结论：设计思路未过时，但在部分场景已有更现代的替代方案。**

| 场景 | Variable 模块 | 现代替代 |
|---|---|---|
| 黑板/配置树（DataNode） | ✅ 仍然适用 | 无更好替代 |
| GC 敏感的高频数据传递 | ✅ 仍然适用 | — |
| UI 数据绑定、属性监听 | ⚠️ 需额外事件代码 | `ReactiveProperty<T>`（R3/UniRx）更简洁 |
| 简单全局变量存储 | ⚠️ 略显重型 | `ScriptableObject` 变量（Ryan Hipple 模式）更直观 |
| 运行时可观测状态机数据 | ❌ 不适用 | R3 `Observable` + `ReactiveProperty<T>` |

> 本项目依赖 **R3**，对于需要响应式订阅的场景（如 UI 血条随 HP 变化），推荐使用 R3 的 `ReactiveProperty<T>`，而黑板/共享状态存储仍可使用 Variable + DataNode。

---

## 六、学习路线建议

### 基础阶段（理解设计意图）

1. **阅读 `IReference` 接口和 `ReferencePool` 实现**
   路径：`GameFramework/Base/ReferencePool/`
   理解：为什么 Variable 要实现 IReference？对象池如何管理生命周期？

2. **阅读 `DataNodeManager` 及其 `DataNode`**
   路径：`GameFramework/DataNode/`
   理解：Variable 在黑板树中如何被存储、读取、释放？

3. **C# 泛型约束与协变**
   理解：为什么 `Variable<T>` 不能直接赋值给 `Variable<object>`？抽象基类如何解耦？

### 进阶阶段（横向对比）

4. **学习 Ryan Hipple 的 ScriptableObject Architecture**
   参考：[Unite Austin 2017 - Game Architecture with Scriptable Objects](https://www.youtube.com/watch?v=raQ3iHhE_Kk)
   类比：同样是"变量封装"思想，但侧重 Unity Inspector 可视化编辑。

5. **学习 Unity Animator 的参数黑板机制**
   理解：`animator.SetFloat("Speed", 5f)` 背后与本模块的相似设计。

6. **学习行为树黑板（Behavior Tree Blackboard）**
   推荐库：Unity 官方 Behavior Trees 包 / NodeCanvas
   理解：Variable 模块在 AI 黑板中的典型应用。

### 现代化阶段（结合响应式）

7. **学习 R3（本项目已集成）**
   核心概念：`ReactiveProperty<T>`、`Observable`、`Subscribe`
   理解：何时用 Variable（无订阅、池化），何时用 ReactiveProperty（需订阅、UI绑定）。

8. **实践：用两种方式实现"玩家HP变化驱动血条UI"**
   - 方式 A：`VarInt` + `GameFramework.Event` 手动分发
   - 方式 B：`ReactiveProperty<int>` + `.Subscribe(hp => hpBar.fillAmount = hp / maxHp)`
   对比体验两者的代码量与维护成本。

### 推荐资源

| 资源 | 说明 |
|---|---|
| [GameFramework 官网](https://gameframework.cn/) | 框架作者 Jiang Yin 的文档与示例 |
| [R3 GitHub](https://github.com/Cysharp/R3) | 本项目使用的响应式框架 |
| 《Game Programming Patterns》Robert Nystrom | 对象池、类型对象等模式的权威讲解（免费在线版） |
| Unity 官方 Scriptable Object 视频 | Ryan Hipple Unite Austin 2017 |
