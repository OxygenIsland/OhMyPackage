# GameFramework Network 模块解析

> 来源：`Assets/Scripts/GameFramework/Network`  
> 原作者：Jiang Yin © 2013-2021 gameframework.cn

---

## 一、模块概览

这是 [GameFramework](https://gameframework.cn/) 框架的**网络通信模块**，为 Unity 游戏提供了一套完整的、可扩展的长连接网络层封装。整个模块基于 **TCP Socket** + **异步 I/O**，采用"管理器 → 频道 → 辅助器"三层架构，将连接管理、数据收发、心跳保活、错误处理全部内聚在一个独立模块中。

---

## 二、文件结构与职责

| 文件 | 类型 | 职责说明 |
|------|------|----------|
| `INetworkManager.cs` | 接口 | 网络管理器对外契约：创建/销毁/查询频道，订阅全局网络事件 |
| `NetworkManager.cs` | 实现 | 管理器核心：持有所有 `NetworkChannelBase` 实例，转发事件 |
| `INetworkChannel.cs` | 接口 | 单条连接的操作契约：Connect、Close、Send，以及各类统计属性 |
| `NetworkManager.NetworkChannelBase.cs` | 抽象类 | 频道基类：SendState/ReceiveState/HeartBeatState 状态机，队列化发包 |
| `NetworkManager.TcpNetworkChannel.cs` | 实现 | 标准 TCP 异步频道（BeginConnect/BeginSend/BeginReceive APM 模式） |
| `NetworkManager.TcpWithSyncReceiveNetworkChannel.cs` | 实现 | TCP 同步接收变体，用于某些平台/调试场景 |
| `INetworkChannelHelper.cs` | 接口 | **扩展点**：消息包头长度、序列化/反序列化、心跳发送——由业务层实现 |
| `IPacketHeader.cs` | 接口 | 消息包头协议：只暴露 `PacketLength`，具体字段由业务层扩展 |
| `IPacketHandler.cs` | 接口 | 消息处理器：每个协议号对应一个 Handler，解耦协议分发逻辑 |
| `Packet.cs` | 抽象类 | 消息包基类，继承自 `BaseEventArgs`，走引用池复用 |
| `NetworkManager.SendState.cs` | 内部类 | 发送缓冲区状态（64 KB MemoryStream） |
| `NetworkManager.ReceiveState.cs` | 内部类 | 接收缓冲区状态，分两阶段：先读包头再读包体 |
| `NetworkManager.HeartBeatState.cs` | 内部类 | 心跳计时器（默认间隔 30 秒），统计连续丢失次数 |
| `NetworkManager.ConnectState.cs` | 内部类 | 异步连接时的上下文传递（Socket + userData） |
| `ServiceType.cs` | 枚举 | `Tcp` / `TcpWithSyncReceive` |
| `AddressFamily.cs` | 枚举 | IPv4 / IPv6 / Unknown |
| `NetworkErrorCode.cs` | 枚举 | 各类错误码（Socket/Connect/Send/Receive/Serialize/Deserialize） |
| `NetworkConnectedEventArgs.cs` | 事件参数 | 连接成功事件，携带频道引用和用户数据 |
| `NetworkClosedEventArgs.cs` | 事件参数 | 连接关闭事件 |
| `NetworkMissHeartBeatEventArgs.cs` | 事件参数 | 心跳丢失事件，携带连续丢失次数 |
| `NetworkErrorEventArgs.cs` | 事件参数 | 网络错误事件，携带错误码和 SocketError |
| `NetworkCustomErrorEventArgs.cs` | 事件参数 | 业务层自定义错误事件 |

---

## 三、核心设计思想

### 1. 三层架构

```
INetworkManager（管理器）
    └─ 持有多个 INetworkChannel（频道）
            └─ 依赖 INetworkChannelHelper（辅助器，业务实现）
```

- **管理器**：全局单例，统一管理多条连接（如主逻辑服、聊天服、战斗服可各自建立一条频道）。
- **频道**：每条 TCP 连接对应一个独立频道，内部有完整的发送队列、接收缓冲区和心跳状态机。
- **辅助器**：唯一需要业务层实现的扩展点，负责协议的序列化/反序列化和心跳格式。

### 2. 两阶段收包（粘包处理）

接收端按"先读固定长度包头 → 再按包头里的 PacketLength 读包体"的方式工作，天然解决了 TCP 粘包/拆包问题。

### 3. 心跳保活

`HeartBeatState` 在每帧 Update 中累加经过时间，达到阈值后调用 `INetworkChannelHelper.SendHeartBeat()`，连续丢失达到上限则触发 `NetworkMissHeartBeat` 事件，由业务层决定是否断线重连。

### 4. 引用池复用

消息包和事件参数均从 `ReferencePool` 中申请/归还，避免高频收发时的 GC 压力。

---

## 四、行业常见使用示例：多人 RPG 玩家登录与进入游戏

### 场景描述

一款手机 MMORPG，客户端需要与登录服务器建立一条 TCP 长连接，发送登录包，收到角色列表后选角并进入游戏世界。

### 步骤一：实现 INetworkChannelHelper（协议层胶水）

```csharp
// 以 Protobuf 为序列化协议为例
public class GameNetworkChannelHelper : MonoBehaviour, INetworkChannelHelper
{
    // 包头固定 4 字节：2字节协议号 + 2字节包体长度
    public int PacketHeaderLength => 4;

    public void Initialize(INetworkChannel networkChannel) { }
    public void Shutdown() { }
    public void PrepareForConnecting() { }

    public bool SendHeartBeat()
    {
        // 发送心跳包（协议号 = 0）
        var heartbeat = ReferencePool.Acquire<CSHeartBeatPacket>();
        GameEntry.Network.GetNetworkChannel("Game").Send(heartbeat);
        return true;
    }

    public bool Serialize<T>(T packet, Stream destination) where T : Packet
    {
        var gamePacket = packet as GamePacketBase;
        // 写入包头：协议号 + 包体长度
        var body = ProtobufHelper.Serialize(gamePacket.ProtoMessage);
        BinaryWriter writer = new BinaryWriter(destination);
        writer.Write((ushort)gamePacket.ProtoId);
        writer.Write((ushort)body.Length);
        writer.Write(body);
        return true;
    }

    public IPacketHeader DeserializePacketHeader(Stream source, out object customErrorData)
    {
        customErrorData = null;
        BinaryReader reader = new BinaryReader(source);
        var header = ReferencePool.Acquire<GamePacketHeader>();
        header.ProtoId = reader.ReadUInt16();
        header.PacketLength = reader.ReadUInt16();
        return header;
    }

    public Packet DeserializePacket(IPacketHeader packetHeader, Stream source, out object customErrorData)
    {
        customErrorData = null;
        var header = (GamePacketHeader)packetHeader;
        return PacketFactory.Create(header.ProtoId, source);
    }
}
```

### 步骤二：启动时创建频道并订阅事件

```csharp
// GameEntry 或 NetworkComponent 初始化时
INetworkChannel loginChannel = GameEntry.Network.CreateNetworkChannel(
    "Login",
    ServiceType.Tcp,
    gameObject.GetComponent<GameNetworkChannelHelper>()
);

GameEntry.Network.NetworkConnected    += OnNetworkConnected;
GameEntry.Network.NetworkClosed       += OnNetworkClosed;
GameEntry.Network.NetworkMissHeartBeat += OnNetworkMissHeartBeat;
GameEntry.Network.NetworkError        += OnNetworkError;
```

### 步骤三：连接并发包

```csharp
// 连接登录服
loginChannel.Connect(IPAddress.Parse("123.45.67.89"), 8000, null);

// 连接成功回调后发登录包
void OnNetworkConnected(object sender, NetworkConnectedEventArgs e)
{
    var loginReq = ReferencePool.Acquire<CSLoginRequest>();
    loginReq.Account  = "player001";
    loginReq.Token    = AuthTokenCache.Token;
    e.NetworkChannel.Send(loginReq);
}
```

### 步骤四：注册 Handler 处理服务器响应

```csharp
// 实现 IPacketHandler
public class SCLoginResponseHandler : IPacketHandler
{
    public int Id => ProtoIds.SC_LOGIN_RESPONSE;   // 协议号

    public void Handle(object sender, Packet packet)
    {
        var resp = (SCLoginResponse)packet;
        if (resp.ResultCode == 0)
            ProcedureManager.EnterProcedure<ProcedureSelectRole>();
        else
            UIManager.ShowTip("账号或密码错误");
    }
}
```

---

## 五、模块评价：在当今行业中过时了吗？

### 优点

| 优点 | 说明 |
|------|------|
| **设计成熟、职责清晰** | 管理器/频道/辅助器的三层拆分，经过数年商业验证 |
| **多频道支持** | 一个客户端同时维护多条 TCP 连接（逻辑服/聊天服/战斗服）毫无压力 |
| **粘包处理正确** | 两阶段读取（包头 → 包体）是业界标准做法 |
| **心跳机制完备** | 自动计时、事件通知，断线重连逻辑交给业务层，灵活 |
| **扩展性强** | `INetworkChannelHelper` 让协议格式与框架完全解耦，Protobuf/FlatBuffers/自定义二进制均可接入 |
| **引用池零 GC** | 高频收发场景友好 |

### 局限与"过时"的地方

| 问题 | 说明 |
|------|------|
| **仅支持 TCP** | 当今竞技类、帧同步游戏大量使用 **UDP（KCP/ENet/Quic）**；该模块不支持 UDP，需要自行扩展或替换 |
| **APM 异步模式** | 使用 `BeginConnect/BeginReceive` 等老式 APM 模式，而非 .NET 现代的 `async/await + SocketAsyncEventArgs`，性能略低且代码可读性较差 |
| **没有 WebSocket** | 小游戏/H5/WebGL 平台强制要求 WebSocket；该模块无法直接用于这些平台 |
| **无内置加密/压缩** | 协议安全和压缩完全依赖业务层在 `INetworkChannelHelper` 中自行处理 |
| **不支持 HTTP/REST** | 纯 Socket 方案，对于 HTTP 接口（充值、排行榜等）需要配合其他组件 |

### 总结性评价

> **不过时，但有边界。**

对于**中大型手游的主逻辑 TCP 长连接场景**（MMORPG、卡牌、SLG），这套模块至今仍是业界主流的架构模式，GameFramework 本身在国内有数百款上线游戏采用。但若项目需要**帧同步对战（UDP）、WebGL 平台（WebSocket）或现代异步编程风格**，则需要在此基础上扩展或引入专门的方案（如 Mirror Networking、FishNet、DOTS Netcode）。

---

## 六、学习路线建议

### 阶段一：基础储备（1-2 个月）

1. **计算机网络基础**  
   - TCP/IP 三次握手、四次挥手、滑动窗口  
   - 粘包/拆包的本质与常见解决方案（固定包头、分隔符、定长）  
   - 推荐书籍：《计算机网络：自顶向下方法》（Kurose & Ross）

2. **C# Socket 编程**  
   - 同步 Socket → APM（Begin/End）→ `SocketAsyncEventArgs` → `async/await`  
   - 理解 `MemoryStream`、`BinaryReader/Writer`

### 阶段二：深入 GameFramework Network（2-4 周）

1. 逐行阅读 `NetworkManager.NetworkChannelBase.cs`，理解发送队列与接收状态机
2. 自己实现一个 `INetworkChannelHelper`，对接 Protobuf 或 MessagePack
3. 搭建一个简单的 C# 服务器（可用 .NET `TcpListener`），和客户端完成完整的收发闭环
4. 练习：添加一个 UDP 频道，继承 `NetworkChannelBase` 扩展 `ServiceType.Udp`

### 阶段三：协议设计与序列化（3-4 周）

1. **Protobuf-net / Google.Protobuf**：定义 `.proto` 文件，生成 C# 代码
2. **FlatBuffers**：零拷贝反序列化，适合帧同步高频小包
3. 了解协议版本兼容设计（向前/向后兼容的字段扩展规则）

### 阶段四：高级网络方案（1-2 个月）

| 方向 | 推荐内容 |
|------|----------|
| **UDP 可靠传输** | 学习 KCP 协议原理；Unity 可用 kcp-csharp 或 ENet |
| **WebSocket** | 了解 RFC 6455，Unity WebGL 平台需使用 JS 桥接 WebSocket |
| **帧同步网络** | 研究 Lockstep 模型；阅读《帧同步游戏开发》相关文章 |
| **服务器端** | 学习 .NET 高性能 IO：`System.IO.Pipelines` + `SocketAsyncEventArgs` |
| **现代 Unity 网络** | 了解 Unity Netcode for GameObjects、FishNet、Mirror 的架构区别 |

### 阶段五：生产实践

1. 实现断线重连与消息重传机制
2. 实现消息加密（AES 对称加密 + RSA 握手交换密钥）
3. 压测：模拟高频收发，使用 Unity Profiler + Memory Profiler 观察 GC 行为
4. 参与开源项目：为 GameFramework 或 Mirror 提交 Issue/PR

---

## 七、相关参考资源

- [GameFramework 官网](https://gameframework.cn/)
- [GameFramework GitHub](https://github.com/EllanJiang/GameFramework)
- [KCP 协议原理（中文）](https://github.com/skywind3000/kcp/blob/master/README.zh-cn.md)
- [Mirror Networking](https://github.com/MirrorNetworking/Mirror)
- [FishNet - Unity Networking](https://github.com/FirstGearGames/FishNet)
- [《Unity 游戏开发》— Mike Geig](https://www.oreilly.com/library/view/unity-game-development/9781491979006/)
