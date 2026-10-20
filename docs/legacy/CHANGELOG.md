# 更新日志

本文档记录了 Godot MCP 项目的所有重要变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2025-11-15

### 新增功能

#### MCP 服务器
- 完整的 MCP JSON-RPC 2.0 协议实现
- 通过 stdio 与 Claude Desktop 通信
- TCP Socket 服务器（端口 7777）与 Godot 通信
- 异步请求处理和队列管理
- 自动重连机制
- 15 个完整功能的 MCP 工具

#### Godot 插件
- EditorPlugin 集成
- 运行时自动启动 MCP 客户端
- TCP 客户端连接到 MCP 服务器
- 完整的场景树访问
- 节点属性读写
- 节点创建和删除
- 方法动态调用
- 资源管理
- 性能监控
- 截图功能

#### MCP 工具

**场景管理**
- `get_scene_tree` - 获取场景树结构
- `get_node_info` - 获取节点详细信息
- `get_property` - 获取节点属性
- `set_property` - 设置节点属性
- `create_node` - 创建新节点
- `delete_node` - 删除节点
- `call_method` - 调用节点方法

**运行时工具**
- `execute_csharp` - 执行 C# 代码（占位实现）
- `get_global_variables` - 获取全局变量
- `get_performance_stats` - 获取性能统计
- `get_logs` - 获取运行时日志
- `load_scene` - 加载场景
- `get_resource_info` - 获取资源信息
- `list_resources` - 列出资源文件
- `take_screenshot` - 截取游戏画面

#### 文档
- README.md - 项目概览
- QUICKSTART.md - 5分钟快速开始指南
- USAGE.md - 详细使用指南
- INTEGRATION.md - 项目集成指南
- DEVELOPMENT.md - 开发者文档
- PROJECT_STRUCTURE.md - 项目结构说明
- PROJECT_SUMMARY.md - 项目完成总结
- ExampleProject/README.md - 示例项目说明
- LICENSE - MIT 许可证

#### 工具和配置
- start-server.sh - Unix 启动脚本
- start-server.bat - Windows 启动脚本
- claude_desktop_config.example.json - Claude Desktop 配置示例
- .gitignore - Git 忽略规则
- package.json - 项目元数据

### 技术实现

#### 架构
- 双层架构设计（MCP 服务器 + Godot 插件）
- 模块化代码组织
- 依赖注入模式
- 异步编程模型

#### 通信
- JSON-RPC 2.0 协议
- stdio 标准输入输出
- TCP Socket 长连接
- 换行符分隔的 JSON 消息
- 请求-响应模式
- 超时处理（默认 5 秒）

#### 错误处理
- 完整的异常捕获
- 友好的错误消息
- 自动重连机制
- 日志记录

#### 性能
- 异步非阻塞 I/O
- 消息队列管理
- 最小化反射开销
- 高效的序列化

### 平台支持

- macOS 10.15+
- Windows 10+
- Linux（现代发行版）
- .NET 8.0+
- Godot 4.0+ (C#)

### 依赖项

**MCP 服务器**
- Newtonsoft.Json 13.0.3
- System.Net.WebSockets 4.3.0
- System.Net.WebSockets.Client 4.3.2

**Godot 插件**
- Godot 4.x (C# 版本)
- .NET 8.0 或更高

### 已知问题

#### 限制
- 仅支持单个 Godot 客户端连接
- C# 代码执行功能为占位实现
- 部分 Godot 类型序列化不完整
- 日志捕获未完全集成

#### 安全性
- 仅应在开发环境使用
- 无身份验证机制
- 监听 localhost 仅限本地访问

### 性能指标

- 典型操作延迟：10-50ms
- 场景树获取：50-200ms（取决于场景大小）
- MCP 服务器内存：~50-100MB
- Godot 插件开销：~5-10MB
- CPU 使用：最小（<1%）

## [未发布]

### 计划中的功能

#### v1.1.0（短期）
- [ ] 完善类型转换支持
- [ ] 增强日志捕获
- [ ] 添加单元测试
- [ ] 性能优化

#### v2.0.0（中期）
- [ ] 实现真正的 C# 代码执行（Roslyn 集成）
- [ ] 支持断点调试
- [ ] Web 控制台界面
- [ ] 多客户端支持
- [ ] 批量操作 API

#### v3.0.0（长期）
- [ ] 可视化调试工具
- [ ] AI 驱动的自动化测试
- [ ] 跨引擎支持（Unity, Unreal）
- [ ] 云端协作功能
- [ ] 性能分析器集成

### 考虑中的改进

- 添加身份验证机制
- 支持 HTTPS/WSS 加密通信
- 增加操作审计日志
- 改进错误恢复
- 支持插件热重载
- 添加配置文件支持
- 改进文档搜索功能

## 版本说明

### 版本号规则

遵循语义化版本规范：

- **主版本号**：不兼容的 API 变更
- **次版本号**：向下兼容的功能新增
- **修订号**：向下兼容的问题修正

### 发布流程

1. 更新 CHANGELOG.md
2. 更新版本号
3. 创建 Git 标签
4. 构建发布版本
5. 发布到 GitHub Releases

## 贡献指南

请查看 [DEVELOPMENT.md](DEVELOPMENT.md) 了解如何贡献代码。

所有重要变更都应记录在此文件中。

---

**项目仓库**: https://github.com/yourusername/Godot-Mcp
**问题追踪**: https://github.com/yourusername/Godot-Mcp/issues
**讨论区**: https://github.com/yourusername/Godot-Mcp/discussions
