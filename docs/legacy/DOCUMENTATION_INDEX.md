# 📚 Godot MCP 文档索引

欢迎查看 **Godot MCP v2.0**！这是 McpServer + GodotPlugin 的合并版本。
> 🎯 **无需 .NET SDK，一个 Godot 插件搞定全部。**

## 🚀 新手必读

如果你是第一次使用 Godot MCP，建议按以下顺序阅读：

1. **[README.md](README.md)** - 了解项目概况 (v2.0 新架构)
2. **[QUICKSTART.md](QUICKSTART.md)** - 5 分钟快速上手（新方式）
3. **[ARCHITECTURE_V5.1.md](ARCHITECTURE_V5.1.md)** - 理解架构设计
4. **[USAGE.md](USAGE.md)** - 学习如何使用各种工具

## 📖 完整文档列表

### 入门指南

| 文档 | 描述 | 适合人群 |
|------|------|----------|
| [README.md](README.md) | 项目概览、功能特性和 v2.0 新架构 | 所有用户 |
| [QUICKSTART.md](QUICKSTART.md) | 5 分钟快速开始指南（v2.0 全新流程） | 新手用户 |
| [ARCHITECTURE_V5.1.md](ARCHITECTURE_V5.1.md) | 架构说明: v1.x→v2.0 变革 + 信号系统设计 | 高级用户 |
| [USAGE.md](USAGE.md) | 详细的使用说明和工具参考 | 普通用户 |

### 集成和配置

| 文档 | 描述 | 适合人群 |
|------|------|----------|
| [INTEGRATION.md](INTEGRATION.md) | 如何将插件集成到现有 Godot 项目 | Godot 开发者 |
| [claude_desktop_config.example.json](claude_desktop_config.example.json) | Claude Desktop 配置示例（v2.0 更新） | 所有用户 |
| [HTTP_API_GUIDE.md](HTTP_API_GUIDE.md) | HTTP API 参考和测试示例 | 开发者 |

### 工具参考

| 文档 | 描述 | 适合人群 |
|------|------|----------|
| [SCENE_QUERY_TOOLS.md](SCENE_QUERY_TOOLS.md) | 场景树查询工具详细介绍 | 所有用户 |
| [SIGNALS_AND_LOGS_GUIDE.md](SIGNALS_AND_LOGS_GUIDE.md) | 信号系统和日志系统指南 | 所有用户 |
| [AI_AGENT_DEBUGGING_GUIDE.md](AI_AGENT_DEBUGGING_GUIDE.md) | AI Agent 调试实战指南 | AI 开发者 |

### 开发和扩展

| 文档 | 描述 | 适合人群 |
|------|------|----------|
| [DEVELOPMENT.md](DEVELOPMENT.md) | 架构说明、扩展开发指南 | 贡献者、高级用户 |
| [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) | 完整的项目结构参考（v2.0 更新） | 开发者 |

### 参考和记录

| 文档 | 描述 | 适合人群 |
|------|------|----------|
| [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) | 项目完成总结和功能清单 | 项目管理者 |
| [CHANGELOG.md](CHANGELOG.md) | 版本历史和更新记录 | 所有用户 |
| [LICENSE](LICENSE) | MIT 开源许可证 | 所有用户 |

### 示例和教程

| 文档 | 描述 | 适合人群 |
|------|------|----------|
| [ExampleProject/README.md](ExampleProject/README.md) | 示例项目说明和测试用例 | 学习者 |
| [test-godot-api.http](test-godot-api.http) | HTTP API 测试用例（curl/http file） | 开发者 |
| [test-new-features.http](test-new-features.http) | 新功能测试用例 | 开发者 |

## 🆕 v2.0 新增/变更

- **`addons/godot_mcp/`** — 合并后的自包含 Godot 插件（核心）
- **`start-mcp-bridge.sh`** — Stdio 桥接脚本（for Claude Desktop）
- **`ARCHITECTURE_V5.1.md`** — 更新了 v1.x→v2.0 架构对比
- **`PROJECT_STRUCTURE.md`** — 更新了 v2.0 项目结构
- **`QUICKSTART.md`** — 重写为 v2.0 安装流程
- **`claude_desktop_config.example.json`** — 更新配置示例
- **`start-server.sh` / `start-server.bat`** — 更新提示信息

## 🎯 按需求查找文档

### 我想...

#### 快速开始使用
→ [QUICKSTART.md](QUICKSTART.md)

#### 了解所有可用工具
→ [USAGE.md](USAGE.md) 中的"MCP 工具完整列表"

#### 将插件添加到我的项目
→ [INTEGRATION.md](INTEGRATION.md)

#### 添加自定义工具
→ [DEVELOPMENT.md](DEVELOPMENT.md) 中的"扩展开发"

#### 了解项目架构
→ [DEVELOPMENT.md](DEVELOPMENT.md) 中的"架构说明"

#### 查看所有文件的用途
→ [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)

#### 了解项目进度
→ [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md)

#### 排查问题
→ [USAGE.md](USAGE.md) 中的"故障排除"

#### 查看版本历史
→ [CHANGELOG.md](CHANGELOG.md)

## 📋 文档分类

### 按角色分类

#### 👤 普通用户
- [README.md](README.md) - 项目介绍
- [QUICKSTART.md](QUICKSTART.md) - 快速开始
- [USAGE.md](USAGE.md) - 使用指南
- [CHANGELOG.md](CHANGELOG.md) - 版本历史

#### 🎮 Godot 开发者
- [INTEGRATION.md](INTEGRATION.md) - 集成指南
- [ExampleProject/README.md](ExampleProject/README.md) - 示例项目
- [USAGE.md](USAGE.md) - 工具参考

#### 💻 贡献者和扩展开发者
- [DEVELOPMENT.md](DEVELOPMENT.md) - 开发文档
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) - 项目结构
- [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) - 实现总结

#### 📊 项目管理者
- [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) - 完成状态
- [CHANGELOG.md](CHANGELOG.md) - 版本管理
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) - 技术栈

### 按主题分类

#### 🔧 安装和配置
- [QUICKSTART.md](QUICKSTART.md) - 安装步骤
- [INTEGRATION.md](INTEGRATION.md) - 集成配置
- [claude_desktop_config.example.json](claude_desktop_config.example.json) - 配置示例

#### 📖 使用说明
- [USAGE.md](USAGE.md) - 详细使用
- [ExampleProject/README.md](ExampleProject/README.md) - 示例演示

#### 🏗️ 架构和设计
- [DEVELOPMENT.md](DEVELOPMENT.md) - 架构文档
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) - 结构说明

#### 📊 项目信息
- [README.md](README.md) - 概览
- [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md) - 总结
- [CHANGELOG.md](CHANGELOG.md) - 历史

## 🔍 快速参考

### 常用命令

#### 启动 MCP 服务器
```bash
# macOS/Linux
./start-server.sh

# Windows
start-server.bat
```

#### 构建项目
```bash
cd McpServer
dotnet build
```

#### 运行服务器
```bash
cd McpServer
dotnet run
```

### 重要路径

#### Claude Desktop 配置
- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`

#### Godot 插件安装位置
```
YourGodotProject/addons/mcp_client/
```

### 默认设置

- **TCP 端口**: 7777
- **超时时间**: 5000ms
- **日志保留**: 1000 条

## 📊 文档统计

- **总文档数**: 10 个
- **总字数**: ~15,000+ 字
- **代码示例**: 50+ 个
- **配置示例**: 10+ 个

## 🆘 获取帮助

### 文档相关

如果文档有不清楚的地方：
1. 查看相关的其他文档
2. 阅读示例项目
3. 查看代码中的注释

### 技术支持

遇到技术问题：
1. 查看 [USAGE.md](USAGE.md) 的故障排除部分
2. 阅读 [DEVELOPMENT.md](DEVELOPMENT.md) 的调试技巧
3. 查看代码仓库的 Issues

## 🔄 文档更新

本文档索引会随着项目更新而更新。

**最后更新**: 2025-11-15
**项目版本**: 1.0.0

## 💡 阅读建议

### 学习路径

#### 初级用户（1-2 小时）
1. README.md（10 分钟）
2. QUICKSTART.md（20 分钟）
3. USAGE.md（30 分钟）
4. 实践操作（30 分钟）

#### 中级用户（3-4 小时）
1. 完成初级路径
2. INTEGRATION.md（30 分钟）
3. ExampleProject/README.md（20 分钟）
4. 创建自己的测试项目（2 小时）

#### 高级用户（5-8 小时）
1. 完成中级路径
2. DEVELOPMENT.md（1 小时）
3. PROJECT_STRUCTURE.md（30 分钟）
4. 阅读源代码（2-3 小时）
5. 实现自定义工具（2 小时）

### 阅读技巧

1. **先浏览后深入**: 先快速浏览了解大纲
2. **实践为主**: 边读边操作效果更好
3. **关注示例**: 代码示例通常最有价值
4. **标记重点**: 用书签标记常用的部分
5. **及时反馈**: 发现问题及时反馈

## 📚 扩展阅读

### 相关资源

- [MCP 规范](https://spec.modelcontextprotocol.io/)
- [Godot 官方文档](https://docs.godotengine.org/)
- [.NET 文档](https://docs.microsoft.com/dotnet/)
- [C# 编程指南](https://docs.microsoft.com/dotnet/csharp/)

### 推荐学习顺序

1. 了解 Godot 基础
2. 学习 C# 基础
3. 理解 MCP 协议
4. 使用 Godot MCP
5. 扩展自定义功能

---

**感谢阅读！希望这些文档能帮助你更好地使用 Godot MCP！** 🎮🤖
