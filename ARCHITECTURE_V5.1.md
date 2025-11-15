# Godot MCP v5.1 架构说明 - 主动记录 vs 被动查询

## 🔄 架构改进

### v5.0 → v5.1 核心变化

**v5.0 架构** (旧):
- MCP 工具主动发起 "开始监听" 请求
- Godot 插件被动响应,临时记录信号
- 需要 AI 提前知道要监听什么

**v5.1 架构** (新):
- Godot 插件启动时自动开始全局监听
- 自动记录所有信号到缓冲区/文件
- MCP 工具只负责查询历史数据
- AI 可以随时查询,无需提前准备

---

## 🎯 设计理念

### 问题场景

**AI Agent 的困境**:
```
AI: 我想知道玩家在过去 5 分钟内死了几次
但我无法提前知道需要监听 "died" 信号
游戏已经运行了,现在开始监听已经晚了
```

**解决方案**:
```
Godot 插件在游戏启动时就开始记录所有信号
AI 随时可以查询历史数据:
"给我过去 5 分钟内,包含 'Player' 节点的 'died' 信号"
```

---

## 📋 职责划分

### Godot 插件职责 (主动维护)

#### 1. 信号系统
```csharp
// 游戏启动时自动执行
_Ready() {
    StartGlobalSignalMonitoring();  // 监听所有节点的所有信号
    InitializeSignalEventsFile();   // 重置信号事件文件
}

// 自动记录信号事件
RecordSignalEvent(node, signalName, args) {
    // 添加到缓冲区
    _signalEventsBuffer.Add(event);
    
    // 缓冲区满时溢出到文件
    if (_signalEventsBuffer.Count > 5000) {
        WriteSignalEventToFile(oldEvent);
    }
}
```

**文件格式** (`user://mcp_signal_events.txt`):
```
=== MCP 信号事件 - 启动时间: 2024-01-01 10:00:00 ===
[2024-01-01 10:05:23.456] /root/Game/Player (CharacterBody2D) :: died []
[2024-01-01 10:08:15.789] /root/Game/Player (CharacterBody2D) :: died []
[2024-01-01 10:15:30.123] /root/Game/Player (CharacterBody2D) :: health_changed [75, 100]
```

#### 2. 日志系统
```csharp
// 环形缓冲区
LinkedList<LogEntry> _logBuffer;  // 最近 1000 条

// 自动记录日志
LogInfo(message) {
    AddLog("info", message);
    GD.Print(message);
}

AddLog(level, message) {
    // 添加到缓冲区
    _logBuffer.AddLast(entry);
    
    // 缓冲区满时溢出到文件
    if (_logBuffer.Count > 1000) {
        WriteLogToFile(oldEntry);
    }
}
```

**文件格式** (`user://mcp_logs.txt`):
```
=== MCP 日志 - 启动时间: 2024-01-01 10:00:00 ===
[2024-01-01 10:05:23] [INFO] 游戏开始
[2024-01-01 10:05:24] [WARNING] 内存使用过高
[2024-01-01 10:05:25] [ERROR] 加载资源失败
```

---

### MCP 服务器职责 (被动查询)

#### 1. 信号查询工具

**get_signal_events** - 查询历史信号事件

```csharp
[McpServerTool]
public async Task<string> GetSignalEvents(
    int count = 50,
    string? nodePath = null,      // 部分匹配
    string? signalName = null,
    long? startTime = null,       // Unix 时间戳
    long? endTime = null)
{
    // 发送查询请求到 Godot
    // Godot 从缓冲区 + 文件中查找
    return await _godotClient.GetSignalEventsAsync(...);
}
```

**查询示例**:
```json
// 查询游戏开始后 5 分钟内玩家死亡次数
{
  "nodePath": "Player",
  "signalName": "died",
  "startTime": 1735689600,
  "endTime": 1735689900,
  "count": 1000
}
```

**start_signal_monitoring** - 设置过滤器 (可选)

```csharp
[McpServerTool]
public async Task<string> StartSignalMonitoring(
    string? signalName = null)  // 仅记录特定信号
{
    // 设置过滤器,减少记录量
    // 默认记录所有信号
}
```

#### 2. 日志查询工具

**get_logs_filtered** - 查询历史日志

```csharp
[McpServerTool]
public async Task<string> GetLogsFiltered(
    string? level = null,
    string? messagePattern = null,
    long? startTime = null,
    long? endTime = null,
    int maxCount = 100)
{
    // Godot 从缓冲区 + 文件中查找
}
```

---

## 🔍 查询流程

### 信号事件查询流程

```
AI Agent 请求
    ↓
MCP Server (GetSignalEvents 工具)
    ↓ HTTP POST /get_signal_events
Godot Plugin
    ↓
ReadAllSignalEvents() {
    1. 从内存缓冲区读取 (最近 5000 条)
    2. 从文件读取 (更早的事件)
    3. 合并 + 排序
    4. 应用过滤条件:
       - nodePath (部分匹配)
       - signalName (精确匹配)
       - startTime / endTime (时间范围)
    5. 返回最近 N 条
}
    ↓
返回 JSON 响应
    ↓
AI Agent 分析
```

---

## 📊 数据流示例

### 场景: AI 调查玩家死亡原因

**时间线**:
```
10:00:00 - 游戏启动 (Godot 自动开始记录)
10:05:23 - 玩家第一次死亡 (信号: Player.died)
10:08:15 - 玩家第二次死亡 (信号: Player.died)
10:10:00 - AI 介入调查
```

**AI 查询**:
```json
POST /get_signal_events
{
  "nodePath": "Player",
  "signalName": "died",
  "count": 10
}
```

**Godot 处理**:
1. 从缓冲区查找 "Player" + "died" 事件
2. 从文件查找 (如果有)
3. 返回:
```json
{
  "matchedEvents": 2,
  "events": [
    {
      "timestamp": "2024-01-01 10:05:23.456",
      "unixTimestamp": 1735689923,
      "nodePath": "/root/Game/Player",
      "signalName": "died"
    },
    {
      "timestamp": "2024-01-01 10:08:15.789",
      "unixTimestamp": 1735690095,
      "nodePath": "/root/Game/Player",
      "signalName": "died"
    }
  ]
}
```

**AI 进一步查询死亡前的事件**:
```json
POST /get_signal_events
{
  "nodePath": "Player",
  "startTime": 1735689918,  // 死亡前 5 秒
  "endTime": 1735689923,    // 死亡时刻
  "count": 50
}
```

**分析**:
```
10:05:18 - Player.health_changed [50, 100]
10:05:20 - Player.health_changed [25, 50]
10:05:22 - Player.health_changed [0, 25]
10:05:23 - Player.died []
```

结论: 玩家在 5 秒内连续受到伤害导致死亡

---

## 💾 数据持久化策略

### 环形缓冲区机制

**信号事件**:
- 内存: LinkedList (最近 5000 个)
- 文件: user://mcp_signal_events.txt (更早的事件)
- 每次启动重置文件

**日志**:
- 内存: LinkedList (最近 1000 条)
- 文件: user://mcp_logs.txt (更早的日志)
- 每次启动重置文件

**优势**:
1. ✅ 快速访问最近数据 (内存)
2. ✅ 完整历史记录 (文件)
3. ✅ 内存可控 (环形缓冲区)
4. ✅ 无需手动管理 (自动溢出)
5. ✅ 每次启动干净 (避免文件膨胀)

---

## 🎯 关键优势

### 1. AI Agent 无需提前准备
```
旧方式:
AI: "我要监听玩家死亡事件" → start_monitoring
[等待事件发生]
AI: "查询事件" → get_events

新方式:
[游戏已运行 10 分钟]
AI: "查询过去 10 分钟内玩家死亡事件" → get_events
✅ 直接得到历史数据
```

### 2. 支持时间范围查询
```
AI: "游戏开始后前 5 分钟发生了什么?"
→ startTime = 游戏启动时间
→ endTime = 启动时间 + 300 秒
```

### 3. 部分匹配查询
```
AI: "查询包含 Player 的节点"
→ nodePath = "Player"
→ 匹配: /root/Game/Player, /root/UI/PlayerUI, etc.
```

### 4. 自动管理,无泄漏
- 环形缓冲区自动淘汰旧数据
- 每次启动重置文件
- 无需手动清理

---

## 🔧 实现细节

### Godot 插件核心代码

```csharp
public partial class McpClient : Node
{
    // 信号事件缓冲区
    private readonly List<SignalEvent> _signalEventsBuffer = new();
    private const int MaxSignalEventsBufferSize = 5000;
    
    public override void _Ready()
    {
        // 初始化文件
        InitializeSignalEventsFile();
        
        // 启动全局监听
        StartGlobalSignalMonitoring();
    }
    
    private void StartGlobalSignalMonitoring()
    {
        // 连接场景树的 node_added 信号
        GetTree().NodeAdded += OnNodeAddedToTree;
        
        // 监听已存在的节点
        MonitorExistingNodes(GetTree().Root);
    }
    
    private void OnNodeAddedToTree(Node node)
    {
        // 为所有信号添加监听
        foreach (var signal in node.GetSignalList())
        {
            node.Connect(signalName, 
                Callable.From(() => RecordSignalEvent(node, signalName, null)));
        }
    }
    
    private void RecordSignalEvent(Node node, string signalName, object[]? args)
    {
        var evt = new SignalEvent {
            Timestamp = DateTime.Now,
            NodePath = node.GetPath().ToString(),
            NodeType = node.GetType().Name,
            SignalName = signalName,
            Args = args?.Select(a => a.ToString()).ToList() ?? new()
        };
        
        _signalEventsBuffer.Add(evt);
        
        // 缓冲区满时溢出
        if (_signalEventsBuffer.Count > MaxSignalEventsBufferSize)
        {
            WriteSignalEventToFile(_signalEventsBuffer[0]);
            _signalEventsBuffer.RemoveAt(0);
        }
    }
}
```

---

## 📈 性能影响

### 内存使用
- 信号缓冲区: ~5000 events × 200 bytes = 1 MB
- 日志缓冲区: ~1000 logs × 150 bytes = 150 KB
- **总计**: ~1.2 MB (可忽略)

### CPU 开销
- 信号记录: < 0.1 ms per event
- 文件写入 (溢出时): < 5 ms
- 对游戏性能影响: **可忽略**

### 磁盘使用
- 信号事件文件: 取决于游戏长度 (通常 < 10 MB)
- 日志文件: 通常 < 1 MB
- 每次启动重置,不会累积

---

## 🎓 使用建议

### AI Agent 最佳实践

**场景 1: 调试当前问题**
```
1. 直接查询最近事件 (无需时间范围)
2. 逐步缩小范围 (添加 nodePath, signalName 过滤)
3. 分析事件序列
```

**场景 2: 分析历史问题**
```
1. 确定问题发生的时间范围
2. 使用 startTime/endTime 精确查询
3. 查看问题前后的事件
```

**场景 3: 性能分析**
```
1. 查询特定时间段的所有信号
2. 统计信号触发频率
3. 找到异常频繁的信号
```

---

## ✅ 总结

**v5.1 架构优势**:
1. ✅ **AI 友好**: 无需提前准备,随时查询历史
2. ✅ **完整记录**: 游戏启动后的所有信号和日志
3. ✅ **灵活查询**: 时间范围 + 节点 + 信号多维度过滤
4. ✅ **自动管理**: 环形缓冲区 + 文件溢出,无需手动维护
5. ✅ **性能友好**: 内存和 CPU 开销可忽略

**适用场景**:
- 🐛 事后调试 (游戏已运行一段时间)
- 📊 性能分析 (查询特定时间段)
- 🔍 事件追踪 (查询信号触发顺序)
- 💾 历史回溯 (分析过去发生的事情)
