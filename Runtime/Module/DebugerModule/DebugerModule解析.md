# DebugerModule 模块解析

> 文档生成时间：2026-05-18

---

## 一、模块总览

`DebugerModule` 是一个 **运行时调试器面板模块**，基于 Unity 的 **IMGUI（Legacy OnGUI）** 系统绘制，提供一套可扩展的、层次化的调试窗口体系。它的定位是"开发期/测试期的设备内可视化调试工具"，无需连接外部工具即可在真机上实时查看运行状态。

---

## 二、文件结构与职责

### 核心文件

| 文件 | 职责 |
|------|------|
| `IDebuggerModule.cs` | 调试器模块对外接口，定义窗口注册/注销/查询/选中 API |
| `IDebuggerWindow.cs` | 单个调试窗口接口：初始化、关闭、进入/离开、Update、Draw |
| `IDebuggerWindowGroup.cs` | 调试窗口分组接口，支持按路径树形组织窗口 |
| `DebuggerModule.cs` | 模块实现类，负责激活状态管理、每帧轮询、窗口注册表维护 |
| `Debugger.cs` | MonoBehaviour 入口，持有所有内置窗口实例，驱动 IMGUI 绘制，读写 PlayerPrefs 保存窗口位置/缩放 |
| `DebuggerActiveWindowType.cs` | 枚举：`AlwaysOpen / OnlyOpenWhenDevelopment / OnlyOpenInEditor / AlwaysClose` |
| `DebuggerManager.DebuggerWindowGroup.cs` | 树形窗口组实现，支持 `"Information/System"` 这样的路径注册 |
| `DebuggerComponent.ConsoleWindow.cs` | 实时日志控制台：Info/Warning/Error/Fatal 四级过滤，颜色标注，滚动锁定，最大行数限制 |
| `DebuggerComponent.FpsCounter.cs` | FPS 计数器，可配置刷新间隔（默认 0.5 秒） |
| `DebuggerSkin.guiskin` | 自定义 IMGUI 皮肤，保证调试面板在不同分辨率下的视觉一致性 |

### Component/ 子窗口

| 窗口类 | 展示内容 |
|--------|---------|
| `SystemInformationWindow` | 设备唯一 ID、设备名、CPU 核心数/频率、系统内存、OS 版本、电池状态 |
| `EnvironmentInformationWindow` | 应用版本、Unity 版本、平台、公司名、安装模式 |
| `ScreenInformationWindow` | 分辨率、DPI、方向、全屏状态 |
| `GraphicsInformationWindow` | GPU 型号、显存大小、图形 API 版本、着色器等级 |
| `InputSummaryInformationWindow` | 输入综合信息（触摸点数量等） |
| `InputTouchInformationWindow` | 当前帧所有触摸点坐标/状态 |
| `InputLocationInformationWindow` | GPS 位置服务数据 |
| `InputAccelerationInformationWindow` | 加速度计数据 |
| `InputGyroscopeInformationWindow` | 陀螺仪数据 |
| `InputCompassInformationWindow` | 指南针朝向数据 |
| `PathInformationWindow` | `Application.persistentDataPath` 等路径 |
| `SceneInformationWindow` | 当前加载场景列表 |
| `TimeInformationWindow` | `Time.deltaTime`、`timeScale`、运行时长 |
| `QualityInformationWindow` | 当前画质等级、阴影/抗锯齿等设置 |
| `ProfilerInformationWindow` | Mono 堆大小、总分配内存、总保留内存、图形驱动内存等 Profiler 数据 |
| `RuntimeMemorySummaryWindow` | 手动采样：按 Unity Object 类型汇总数量与内存占用 |
| `RuntimeMemoryInformationWindow<T>` | 特定类型（Texture/Mesh/Material/Shader/AudioClip/Font 等）的详细内存列表 |
| `ObjectPoolInformationWindow` | 对象池各类型的使用/空闲数量 |
| `MemoryPoolInformationWindow` | 内存池状态 |
| `SettingsWindow` | 调试器自身设置（窗口缩放比例、FPS 更新频率） |
| `ScrollableDebuggerWindowBase` | 所有信息窗口的带滚动条基类 |

---

## 三、架构设计

```
Debugger (MonoBehaviour)
  └── IDebuggerModule (DebuggerModule)
        └── DebuggerWindowGroup (根节点，树形路径)
              ├── "Console"                    → ConsoleWindow
              ├── "Information"                → DebuggerWindowGroup
              │     ├── "System"               → SystemInformationWindow
              │     ├── "Environment"          → EnvironmentInformationWindow
              │     ├── "Input/Touch"          → InputTouchInformationWindow
              │     └── ...
              ├── "Profiler"                   → ProfilerInformationWindow
              ├── "Memory/Summary"             → RuntimeMemorySummaryWindow
              └── "Settings"                   → SettingsWindow
```

**扩展机制**：实现 `IDebuggerWindow` 接口，调用 `RegisterDebuggerWindow("MyGroup/MyWindow", new MyWindow())` 即可挂载自定义窗口，完全解耦。

---

## 四、行业常见使用示例

### 场景：手游上线前真机压测

某 3D 手游上线前需要在 Android 低端机上进行兼容性与内存压测，测试同学没有 PC 和 Unity Editor，只有一台手机。

**步骤：**

**1. 在 Launcher 场景预制体上挂载 Debugger**

在游戏启动的根场景预制体上添加 `Debugger` 组件，设置：
```
ActiveWindow = OnlyOpenWhenDevelopment   // 仅开发包打开，Release 自动关闭
WindowScale  = 1.8f                      // 适配低分辨率小屏
```

**2. 注册自定义窗口（例：网络延迟监控）**

```csharp
// 在 GameApp（热更入口）中注册
public class NetworkDebugWindow : IDebuggerWindow
{
    public void Initialize(params object[] args) { }
    public void Shutdown() { }
    public void OnEnter() { }
    public void OnLeave() { }
    public void OnUpdate(float elapseSeconds, float realElapseSeconds) { }

    public void OnDraw()
    {
        GUILayout.Label($"RTT: {NetworkManager.Instance.Rtt} ms");
        GUILayout.Label($"PacketLoss: {NetworkManager.Instance.PacketLoss:P1}");
        if (GUILayout.Button("断开重连"))
            NetworkManager.Instance.Reconnect();
    }
}

// 注册到 "Network/Stats" 路径
Debugger.Instance.RegisterDebuggerWindow("Network/Stats", new NetworkDebugWindow());
```

**3. 测试流程**

- 打开 Memory/Summary 面板 → 点击 `Take Sample` → 记录初始内存基线
- 进入副本战斗 5 分钟 → 再次 `Take Sample` → 对比 Texture/AudioClip 增长
- 打开 Profiler 面板 → 观察 Mono Heap 是否持续增长（GC 泄漏判断）
- 打开 Console 面板 → 过滤 Error/Fatal → 快速定位崩溃前日志
- 打开 Information/System 面板 → 记录设备参数，确认是否命中低端机阈值

---

## 五、模块评价

### 优点

| 方面 | 评价 |
|------|------|
| **零依赖** | 纯 IMGUI 实现，不依赖 UGUI/UIToolkit，任何场景都可运行 |
| **可扩展** | `IDebuggerWindow` 接口极简，新增窗口成本极低 |
| **设备内可视** | 真机无需 PC 即可查看运行数据，对测试团队友好 |
| **开关控制** | `DebuggerActiveWindowType` 枚举精确控制显示时机，Release 包完全关闭无性能损耗 |
| **持久化** | 窗口位置/缩放通过 `PlayerPrefs` 持久化，体验细腻 |
| **内存诊断完备** | 覆盖 Mono 堆、图形内存、按类型采样，对移动端内存优化非常实用 |

### 局限性

| 方面 | 评价 |
|------|------|
| **IMGUI 已是遗留技术** | Unity 官方已推荐迁移到 UI Toolkit（UIElements），IMGUI 在复杂 UI 上性能较差 |
| **触屏体验欠佳** | IMGUI 的触摸操作不如 UGUI 顺滑，小屏幕上按钮点击困难 |
| **无远程访问** | 不支持通过浏览器/外部工具远程查看，这在 CI/CD 自动化测试中是短板 |
| **样式定制成本高** | IMGUI 皮肤修改繁琐，无法像 UGUI 那样美观定制 |

### 是否过时？

**不完全过时，但已处于"实用期后半段"。**

- 在 **中小型项目/个人项目/快速迭代** 阶段，这套方案依然是最轻量的选择，开箱即用。
- 在 **大厂商业项目** 中，现代替代方案更受青睐：
  - **Unity Diagnostics（官方）**：Memory Profiler Package、Profile Analyzer，支持快照对比
  - **Graphy（开源）**：UGUI 实现的 FPS/内存/网络悬浮图表，视觉效果更好
  - **SRDebugger（商业）**：UGUI 实现，支持远程控制台、选项面板，功能最全面
  - **自研 Web 调试后台**：将日志和性能数据实时推送到局域网浏览器，支持多设备同时监控

---

## 六、学习路线建议

### 入门阶段（理解本模块）

1. **阅读 `IDebuggerWindow` 接口** → 理解 `Initialize/Shutdown/OnEnter/OnLeave/OnUpdate/OnDraw` 生命周期
2. **仿写一个最简自定义窗口** → 用 `GUILayout.Label/Button` 展示一个变量，注册到调试器
3. **学习 Unity IMGUI 基础** → `GUILayout`、`GUI.Box`、`GUISkin`、`GUI.Window`

### 进阶阶段（深入框架设计）

4. **研究 `DebuggerWindowGroup` 树形路径注册机制** → 理解路径解析与分组 Tab 切换的关系
5. **研究 `ScrollableDebuggerWindowBase`** → 了解如何用继承+模板方法模式统一滚动容器
6. **阅读 `RuntimeMemorySummaryWindow`** → 学习 `Resources.FindObjectsOfTypeAll` + `Profiler.GetRuntimeMemorySizeLong` 的内存采样方式

### 扩展阶段（现代化演进）

7. **学习 UGUI 实现调试面板** → 参考 Graphy 开源项目，理解如何用 Canvas 绘制实时折线图
8. **学习 UI Toolkit（UIElements）** → Unity 6 推荐的新 UI 系统，在 Editor 工具和 Runtime UI 中均可使用
9. **学习 Unity Memory Profiler Package** → 官方深度内存分析工具，理解快照对比和对象引用链
10. **实现远程调试日志系统** → 用 `UnityWebRequest` 或 `TcpClient` 将日志推送到局域网 Web 页面

### 参考资源

| 资源 | 说明 |
|------|------|
| [Unity IMGUI 官方文档](https://docs.unity3d.com/Manual/GUIScriptingGuide.html) | IMGUI 系统完整参考 |
| [Graphy GitHub](https://github.com/Tayx94/graphy) | UGUI 实现的开源性能调试工具，值得对比学习 |
| [Unity Memory Profiler](https://docs.unity3d.com/Packages/com.unity.memoryprofiler@latest) | 官方内存快照分析包 |
| [UI Toolkit 官方文档](https://docs.unity3d.com/Manual/UIElements.html) | Unity 未来 UI 方向 |
| [GameFramework 源码](https://github.com/EllanJiang/GameFramework) | 本模块的思想来源，完整框架参考 |

---

> **总结**：DebugerModule 是一个设计良好的、轻量级运行时调试器，核心价值在于**可扩展的窗口注册体系**和**完备的内存/性能诊断窗口集合**。对于使用 TEngine/OhMyPackage 框架的项目，它是开发测试阶段不可或缺的工具。随着项目规模扩大，可以在此基础上逐步替换 IMGUI 渲染层，或配合远程日志系统共同使用。
