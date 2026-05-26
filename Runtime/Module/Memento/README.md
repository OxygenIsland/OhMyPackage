# MementoManager for Unity

> 工具链开发主流做法的备忘录模式全局管理器。
> 调研来源：refactoring.guru · postsharp.net · codeproject · UnityCommunity/UnitySingleton

---

## 目录结构

```
MementoManager/
├── Interfaces/
│   └── IMementoInterfaces.cs       ← IMemento / IOriginator / IOriginator<T>
├── Core/
│   ├── Memento.cs                  ← 内部快照实现（泛型，封装数据）
│   ├── HistoryChannel.cs           ← 单 Key 双栈管理（Undo/Redo 指针）
│   ├── MementoManager.cs           ← 全局单例，对外 API 入口
│   └── MementoOriginator.cs        ← MonoBehaviour / 纯C# 便捷基类
├── Serialization/
│   └── StateSerializer.cs          ← JsonUtility 深拷贝工具
├── Editor/
│   └── MementoManagerDebugWindow.cs← 快照历史查看器（Editor 调试）
├── Examples/
│   ├── Example_MapEditor.cs        ← 场景1：关卡编辑器 Undo/Redo
│   ├── Example_GameSave.cs         ← 场景2：游戏存档 & 检查点
│   └── Example_NodeGraphEditor.cs  ← 场景3：节点图编辑器 + EditorWindow
└── Tests/
    └── MementoManagerTests.cs      ← NUnit 单元测试
```

---

## 安装

将整个 `MementoManager` 文件夹放入 `Assets/` 目录即可。  
无需第三方依赖，仅使用 Unity 内置的 `JsonUtility`。

---

## 快速上手

### 方式 A：继承基类（推荐，最简洁）

```csharp
// 1. 定义状态 DTO（必须标注 [Serializable]）
[Serializable]
public class MapState {
    public int width, height;
    public int[] tiles;
}

// 2. 继承 MementoOriginator<T>
public class MapEditor : MementoOriginator<MapState>
{
    public override MapState CaptureState()      => new MapState { width=10, tiles=_tiles };
    public override void     ApplyState(MapState s) { _tiles = s.tiles; }

    void Start() {
        SaveSnapshot("Map", "初始地图");   // 保存
    }
    void OnCtrlZ() {
        UndoSnapshot("Map");              // 撤销
    }
    void OnCtrlY() {
        RedoSnapshot("Map");             // 重做
    }
}
```

### 方式 B：泛型 API（无需继承，最灵活）

```csharp
var mgr = MementoManager.Instance;

// 保存
mgr.Save("Player", playerState, "到达检查点");

// 读取当前状态（不移动指针）
mgr.TryPeekState<PlayerState>("Player", out var state);

// Undo / Redo（返回数据，由调用方应用）
if (mgr.Undo<PlayerState>("Player", out var prev)) ApplyState(prev);
if (mgr.Redo<PlayerState>("Player", out var next)) ApplyState(next);

// 跳转到指定版本（存档槽）
mgr.JumpToVersion<PlayerState>("Player", version: 3, out var slotState);

// 持久化
string json = mgr.ExportCurrentStateJson<PlayerState>("Player");
mgr.ImportStateFromJson<PlayerState>("Player", json, "从磁盘加载");
```

### 方式 C：事件监听

```csharp
MementoManager.Instance.OnMementoChanged += e => {
    Debug.Log($"{e.Action} | Key={e.Key} | {e.Memento?.Label}");
    RefreshUndoButton();
};
```

---

## State DTO 规范

| 规则 | 原因 |
|---|---|
| 必须标注 `[System.Serializable]` | JsonUtility 序列化要求 |
| 只包含值类型 / 基础类型 / `[Serializable]` 子类 | 深拷贝完整性 |
| 不要包含 `UnityEngine.Object` 引用 | 无法序列化，改用 `string` ID |
| 字段使用 `public` 或 `[SerializeField]` | JsonUtility 只识别这两种 |

---

## 核心设计决策

| 决策 | 依据 |
|---|---|
| 使用 `JsonUtility` 深拷贝 | IL2CPP 友好，无第三方依赖，速度最快 |
| 双指针 List 而非两个 Stack | 支持 JumpToVersion 跳转，与 codesociety.net 方案一致 |
| `internal Memento<T>` 封装 | 外部只见 `IMemento`，保证封装性（GoF 备忘录核心约束） |
| `HistoryChannel` 按 Key 隔离 | 不同系统互不干扰，可独立配置容量 |
| `RuntimeInitializeOnLoadMethod` 懒加载 | 不挂 Prefab 也能用，工具链友好 |

---

## Editor 调试

菜单 `Tools > MementoManager > 历史快照查看器`  
在 PlayMode 下实时查看所有 Key 的快照列表，支持一键 Clear。

---

## 运行测试

1. 打开 `Window > General > Test Runner`
2. 选择 `EditMode` 标签
3. 点击 `Run All`
