# Godot MCP 信号和日志调试指南

## 📋 目录

- [简化场景树查询](#简化场景树查询)
- [信号系统调试](#信号系统调试)
- [增强日志系统](#增强日志系统)
- [实战调试场景](#实战调试场景)

---

## 🌲 简化场景树查询

当 AI 只需要快速浏览场景结构时,使用简化版本可以显著减少响应大小。

### get_scene_tree_simple

**用途**: 获取仅包含名称和类型的简化场景树

**参数**:
- `rootPath` (string, 默认 "/root"): 根节点路径
- `maxDepth` (int, 默认 3): 最大深度

**示例**:
```json
{
  "rootPath": "/root",
  "maxDepth": 2
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "name": "root",
    "type": "SceneTree",
    "children": [
      {
        "name": "Main",
        "type": "Node2D",
        "children": [
          { "name": "Player", "type": "CharacterBody2D" },
          { "name": "Camera", "type": "Camera2D" }
        ]
      }
    ]
  }
}
```

**对比完整场景树**:
- ✅ **简化版**: 仅结构和类型,适合快速浏览
- ⚙️ **完整版**: 包含属性、方法、信号,适合详细分析

---

## ⚡ 信号系统调试

Godot 的信号系统是事件驱动的核心,这些工具帮助 AI 理解和调试信号流。

### 🔄 自动监听机制

**Godot 插件在启动时自动:**
1. 监听所有节点的所有信号触发
2. 记录到环形缓冲区 (最近 5000 个事件)
3. 缓冲区满时自动溢出到文件 (`user://mcp_signal_events.txt`)
4. 每次游戏启动时重置文件

**AI Agent 只需查询,无需手动开启监听!**

---

### get_signal_events

**用途**: 查询已记录的信号事件 (支持时间范围查询)

**参数**:
- `count` (int, 默认 50): 返回最近 N 条
- `nodePath` (string, 可选): 按节点路径过滤 (支持部分匹配)
- `signalName` (string, 可选): 按信号名称过滤
- `startTime` (long, 可选): 开始时间 (Unix 时间戳)
- `endTime` (long, 可选): 结束时间 (Unix 时间戳)

**示例 1 - 查询最近 50 个事件**:
```json
{
  "count": 50
}
```

**示例 2 - 查询特定节点的信号**:
```json
{
  "count": 100,
  "nodePath": "Player"  // 部分匹配,会匹配 /root/Main/Player
}
```

**示例 3 - 查询时间范围内的事件**:
```json
{
  "count": 200,
  "startTime": 1735689600,  // 2024-01-01 10:00:00
  "endTime": 1735693200     // 2024-01-01 11:00:00
}
```

**示例 4 - 组合查询**:
```json
{
  "count": 50,
  "nodePath": "Player",
  "signalName": "health_changed",
  "startTime": 1735689600,
  "endTime": 1735693200
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "totalEvents": 3542,
    "matchedEvents": 23,
    "returnedEvents": 20,
    "events": [
      {
        "timestamp": "2024-01-01 10:15:30.123",
        "unixTimestamp": 1735690530,
        "nodePath": "/root/Main/Player",
        "nodeType": "CharacterBody2D",
        "signalName": "health_changed",
        "args": ["75", "100"]
      }
    ]
  }
}
```

**典型用例**:
```
问题: AI 想知道游戏开始后 5 分钟内玩家死了几次

查询:
{
  "nodePath": "Player",
  "signalName": "died",
  "startTime": <游戏开始时间>,
  "endTime": <游戏开始时间 + 300>
}
```

### get_node_signals

**用途**: 获取节点的所有信号及其参数

**参数**:
- `nodePath` (string): 节点路径

**示例**:
```json
{
  "nodePath": "/root/Main/Player"
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "nodePath": "/root/Main/Player",
    "signals": [
      {
        "name": "health_changed",
        "args": ["new_health", "old_health"]
      },
      {
        "name": "died",
        "args": []
      }
    ]
  }
}
```

---

### get_signal_connections

**用途**: 查看信号的所有连接关系

**参数**:
- `sourceNodePath` (string): 源节点路径
- `signalName` (string): 信号名称

**示例**:
```json
{
  "sourceNodePath": "/root/Main/Player",
  "signalName": "health_changed"
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "signal": "health_changed",
    "connections": [
      {
        "signal": "health_changed",
        "callable": "UI::UpdateHealthBar",
        "target": "/root/Main/UI"
      }
    ]
  }
}
```

---

### connect_signal

**用途**: 动态连接信号到方法 (用于运行时调试)

**参数**:
- `sourceNodePath` (string): 源节点路径
- `signalName` (string): 信号名称
- `targetNodePath` (string): 目标节点路径
- `targetMethod` (string): 目标方法名

**示例**:
```json
{
  "sourceNodePath": "/root/Main/Player",
  "signalName": "died",
  "targetNodePath": "/root/McpClient",
  "targetMethod": "OnPlayerDied"
}
```

**用例**: AI 可以临时连接信号来验证事件触发,无需修改代码。

---

### disconnect_signal

**用途**: 断开信号连接

**参数**: 与 `connect_signal` 相同

---

### emit_signal

**用途**: 手动触发信号 (用于测试)

**参数**:
- `nodePath` (string): 节点路径
- `signalName` (string): 信号名称
- `args` (array, 可选): 信号参数

**示例**:
```json
{
  "nodePath": "/root/Main/Player",
  "signalName": "health_changed",
  "args": [50, 100]
}
```

**用例**: 
- 测试信号处理逻辑
- 模拟事件触发
- 验证信号连接

---

### start_signal_monitoring

**用途**: 设置信号过滤器 (控制记录哪些信号)

**参数**:
- `signalName` (string, 可选): 要监听的信号名称 (null = 监听所有)
- `maxEvents` (int, 默认 5000): 缓冲区大小 (自动管理)

**示例 1 - 监听所有信号**:
```json
{
  "signalName": null
}
```

**示例 2 - 仅监听特定信号**:
```json
{
  "signalName": "ready"
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "monitoring": true,
    "monitoredSignals": ["ready"],
    "currentEventsCount": 1523
  }
}
```

**注意**: 
- 全局监听在游戏启动时已自动开启
- 此工具用于设置**过滤器**,控制记录哪些信号
- 不影响已记录的事件

---

### stop_signal_monitoring

**用途**: 查看信号监听统计

**响应**:
```json
{
  "success": true,
  "data": {
    "monitoring": true,
    "note": "全局监听持续运行,可通过设置过滤器控制记录",
    "totalEvents": 3542
  }
}
```

---

### clear_signal_events

**用途**: 清空信号事件记录

---

## 📊 增强日志系统

### 架构说明

**环形缓冲区机制**:
```
内存缓冲区 (最近 1000 条)
    ↓
   满了?
    ↓
写入文件 (user://mcp_logs.txt)
    ↓
每次启动重置
```

**优势**:
- ✅ 内存高效 (仅保留最近 N 条)
- ✅ 历史可查 (溢出写入文件)
- ✅ 无累积膨胀 (每次启动重置)
- ✅ 实时访问 (缓冲区 + 文件)

---

### add_custom_log

**用途**: 添加自定义调试标记

**参数**:
- `message` (string): 日志消息
- `level` (string, 默认 "info"): 日志级别 (info/warning/error/debug)

**示例**:
```json
{
  "message": "=== 开始性能测试 ===",
  "level": "info"
}
```

**用例**: 
- 在代码特定位置插入标记
- 标记测试开始/结束
- 记录关键状态变化

---

### get_logs_filtered

**用途**: 按条件过滤查询日志

**参数**:
- `level` (string, 可选): 日志级别 (error/warning/info/debug)
- `messagePattern` (string, 可选): 消息内容 (部分匹配)
- `startTime` (long, 可选): 开始时间 (Unix 时间戳)
- `endTime` (long, 可选): 结束时间 (Unix 时间戳)
- `maxCount` (int, 默认 100): 最大返回数量

**示例 1 - 查找所有错误**:
```json
{
  "level": "error",
  "maxCount": 50
}
```

**示例 2 - 查找包含特定关键字**:
```json
{
  "messagePattern": "内存",
  "maxCount": 100
}
```

**示例 3 - 时间范围查询**:
```json
{
  "startTime": 1704067200,
  "endTime": 1704153600,
  "maxCount": 200
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "totalMatched": 23,
    "logs": [
      {
        "timestamp": "2024-01-01T12:34:56",
        "level": "error",
        "message": "内存不足: 无法加载纹理"
      }
    ]
  }
}
```

---

### get_log_stats

**用途**: 获取日志统计信息

**响应**:
```json
{
  "success": true,
  "data": {
    "totalLogs": 1523,
    "inBuffer": 1000,
    "inFile": 523,
    "byLevel": {
      "info": 1200,
      "warning": 250,
      "error": 73
    },
    "oldestLog": "2024-01-01T10:00:00",
    "newestLog": "2024-01-01T15:30:00"
  }
}
```

---

### export_logs

**用途**: 导出所有日志到文件

**参数**:
- `filePath` (string, 可选, 默认 "user://logs_export.txt"): 导出路径

**示例**:
```json
{
  "filePath": "user://debug_session_2024.txt"
}
```

**用例**:
- 生成调试报告
- 长期存档
- 离线分析

---

### clear_logs

**用途**: 清空所有日志 (缓冲区 + 文件)

---

## 💡 典型查询示例

### 示例 1: 查询游戏开始后特定信号的触发次数

**场景**: AI 想知道玩家在游戏前 10 分钟内死了几次

```json
POST /get_signal_events
{
  "nodePath": "Player",
  "signalName": "died",
  "startTime": 1735689600,  // 游戏开始时间
  "endTime": 1735690200,    // 10分钟后
  "count": 1000
}
```

**响应**:
```json
{
  "totalEvents": 5234,
  "matchedEvents": 3,
  "returnedEvents": 3,
  "events": [
    {
      "timestamp": "2024-01-01 10:05:23.456",
      "unixTimestamp": 1735689923,
      "nodePath": "/root/Game/Player",
      "nodeType": "CharacterBody2D",
      "signalName": "died",
      "args": []
    },
    {
      "timestamp": "2024-01-01 10:08:15.789",
      "unixTimestamp": 1735690095,
      "nodePath": "/root/Game/Player",
      "nodeType": "CharacterBody2D",
      "signalName": "died",
      "args": []
    },
    {
      "timestamp": "2024-01-01 10:09:45.123",
      "unixTimestamp": 1735690185,
      "nodePath": "/root/Game/Player",
      "nodeType": "CharacterBody2D",
      "signalName": "died",
      "args": []
    }
  ]
}
```

**分析**: 玩家在前 10 分钟死了 3 次

---

### 示例 2: 查询某段时间内所有节点的信号

**场景**: 游戏在 10:15 - 10:16 之间卡顿,查询这段时间发生了什么

```json
POST /get_signal_events
{
  "startTime": 1735690500,
  "endTime": 1735690560,
  "count": 500
}
```

**分析**: 可以看到这段时间内所有信号的触发,找到异常频繁的信号

---

### 示例 3: 查询特定节点在游戏全程的所有信号

**场景**: 追踪 Boss 节点的所有信号触发

```json
POST /get_signal_events
{
  "nodePath": "Boss",  // 部分匹配
  "count": 1000
}
```

---

## 🎯 实战调试场景

### 场景 1: 调试 UI 不更新问题

**问题**: 玩家血量变化时 UI 不更新

**调试流程**:

1. **检查信号定义**
```bash
工具: get_node_signals
节点: /root/Main/Player
目的: 确认 health_changed 信号存在
```

2. **检查信号连接**
```bash
工具: get_signal_connections
信号: health_changed
目的: 确认是否连接到 UI 节点
```

3. **查询信号历史** (无需手动启动监听!)
```bash
工具: get_signal_events
参数: {
  "nodePath": "Player",
  "signalName": "health_changed",
  "count": 50
}
目的: 查看最近是否有信号触发
```

4. **分析结果**:
- ✅ 找到信号触发记录 → 检查 UI 更新逻辑
- ❌ 没有信号触发 → 检查伤害逻辑

**优势**: 
- 不需要提前开启监听
- 可以查询游戏开始后的所有历史事件
- 支持时间范围精确定位问题发生时间

---

### 场景 2: 性能问题排查

**问题**: 游戏运行一段时间后卡顿

**调试流程**:

1. **插入性能标记**
```bash
工具: add_custom_log
消息: "=== 性能测试开始 ==="
```

2. **获取场景树结构**
```bash
工具: get_scene_tree_simple
深度: 3
目的: 快速浏览场景结构
```

3. **运行游戏 5 分钟**

4. **查看性能统计**
```bash
工具: get_performance_stats
```

5. **查看错误日志**
```bash
工具: get_logs_filtered
级别: error
目的: 查找错误消息
```

6. **查看警告日志**
```bash
工具: get_logs_filtered
级别: warning
目的: 查找内存/资源警告
```

7. **导出日志**
```bash
工具: export_logs
路径: user://performance_debug.txt
```

---

### 场景 3: 追踪事件触发顺序

**问题**: 不确定事件触发顺序导致逻辑错误

**调试流程**:

1. **查询特定时间段的所有信号**
```bash
工具: get_signal_events
参数: {
  "count": 200,
  "startTime": <操作开始时间>,
  "endTime": <操作结束时间>
}
```

2. **分析信号时间线**
```
对比:
- 信号触发的时间戳 (精确到毫秒)
- 触发的节点路径和类型
- 预期的顺序
```

3. **示例查询** - 查询玩家死亡前 5 秒的所有信号
```json
{
  "count": 100,
  "nodePath": "Player",
  "startTime": <死亡时间 - 5>,
  "endTime": <死亡时间>
}
```

**优势**:
- 精确到毫秒的时间戳
- 自动记录所有信号,不会遗漏
- 可以事后分析,无需提前准备

---

### 场景 4: 内存泄漏调试

**问题**: 内存使用持续增长

**调试流程**:

1. **添加基线标记**
```bash
工具: add_custom_log
消息: "=== 内存基线测试 ==="
```

2. **记录初始状态**
```bash
工具: get_performance_stats
```

3. **运行游戏 (重复操作)**
```bash
操作: 加载/卸载场景 10 次
```

4. **每次循环添加标记**
```bash
工具: add_custom_log
消息: "循环 N - 场景已卸载"
```

5. **查看性能趋势**
```bash
工具: get_log_stats
目的: 查看日志量增长
```

6. **过滤内存相关日志**
```bash
工具: get_logs_filtered
关键字: "memory|内存|leak"
```

7. **导出完整报告**
```bash
工具: export_logs
```

---

## 🔧 AI Agent 使用建议

### 快速浏览场景结构
```
使用: get_scene_tree_simple (maxDepth=2)
而非: get_scene_tree
原因: 减少 90% 响应大小
```

### 定位问题节点
```
流程:
1. get_scene_tree_simple (快速浏览)
2. search_nodes (精确定位)
3. get_node_info (详细信息)
```

### 调试信号问题
```
标准流程:
1. get_node_signals (查看可用信号)
2. get_signal_connections (检查连接)
3. start_signal_monitoring (开始监听)
4. [执行操作]
5. get_signal_events (查看结果)
```

### 日志分析
```
分层查询:
1. get_log_stats (全局概况)
2. get_logs_filtered (level=error) (错误优先)
3. get_logs_filtered (pattern=关键字) (精确搜索)
4. export_logs (导出完整数据)
```

---

## 📈 工具对比表

| 工具 | 用途 | 响应大小 | 适用场景 |
|------|------|---------|---------|
| get_scene_tree | 完整场景树 | 大 | 详细分析 |
| get_scene_tree_simple | 简化场景树 | 小 | 快速浏览 |
| get_logs | 最近日志 | 中 | 快速检查 |
| get_logs_filtered | 过滤日志 | 小-中 | 精确查询 |
| start_signal_monitoring | 信号监听 | - | 事件追踪 |
| get_signal_events | 信号历史 | 中 | 事件分析 |

---

## 🎓 最佳实践

### 1. 始终从简化版本开始
```
✅ get_scene_tree_simple → 找到节点 → get_node_info
❌ get_scene_tree (直接获取所有数据)
```

### 2. 使用日志标记关键点
```csharp
// AI 可以在关键位置插入标记
await AddCustomLog("进入敌人 AI 更新循环", "debug");
await AddCustomLog("检测到玩家", "info");
```

### 3. 组合使用信号监听和日志
```
开始监听信号
↓
添加日志标记
↓
执行操作
↓
查看信号事件 + 过滤日志
↓
关联分析
```

### 4. 定期清理
```
测试完成后:
- clear_signal_events
- clear_logs

避免数据混淆
```

---

## 🚀 总结

**新增功能统计**:
- 🌲 简化场景树: 1 个工具
- ⚡ 信号系统: 9 个工具
- 📊 日志系统: 6 个工具
- **总计**: 16 个新工具,共 48 个工具

**核心优势**:
1. **Agent 友好**: 简化响应,减少 token 消耗
2. **实时监控**: 信号事件 + 日志追踪
3. **历史回溯**: 环形缓冲区 + 文件持久化
4. **灵活过滤**: 多维度查询能力

**适用场景**:
- 🐛 调试信号连接问题
- 📈 性能分析和优化
- 🔍 事件序列追踪
- 💾 内存泄漏检测
- 🎮 游戏逻辑验证
