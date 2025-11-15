# 🔧 Godot MCP 插件安装修复指南

## 问题诊断

你遇到的问题是：MCP 服务器在等待 Godot 连接，但 Godot 客户端没有启动。

**原因**：EditorPlugin 在游戏运行时不会自动创建子节点。

## ✅ 解决方案（两种方法）

### 方法 1：使用 AutoLoad（推荐，自动化）

我已经更新了 `McpPlugin.cs`，现在它会自动将 McpClient 添加为 AutoLoad。

**步骤**：

1. **确保插件在正确位置**
   ```bash
   # 你的 Godot 项目结构应该是：
   YourGodotProject/
   ├── addons/
   │   └── mcp_client/          # 或 GodotPlugin（你当前的名字）
   │       ├── plugin.cfg
   │       ├── McpPlugin.cs
   │       ├── McpClient.cs
   │       └── RuntimeBridge.cs
   ```

2. **在 Godot 编辑器中**：
   - 项目 -> 项目设置 -> 插件
   - 如果插件已启用，先**禁用**再**重新启用**
   - 应该看到输出："已添加 McpClientAutoload 到 AutoLoad"

3. **验证 AutoLoad 已添加**：
   - 项目 -> 项目设置 -> AutoLoad
   - 应该看到 `McpClientAutoload` 在列表中

4. **运行游戏（F5）**

5. **检查输出**：
   应该看到类似：
   ```
   MCP Client 已启动
   已连接到 MCP 服务器 127.0.0.1:7777
   ```

### 方法 2：手动添加 AutoLoad（如果方法1不工作）

如果自动添加失败，可以手动添加：

1. **在 Godot 编辑器中**：
   - 项目 -> 项目设置 -> AutoLoad
   - 点击文件夹图标
   - 选择 `res://addons/mcp_client/McpClient.cs` 或 `res://addons/GodotPlugin/McpClient.cs`
   - 节点名称输入：`McpClientAutoload`
   - 点击"添加"

2. **运行游戏（F5）**

## 🔍 验证连接成功

### MCP 服务器端应该显示：
```
[12:03:26] [Godot] 等待 Godot 客户端连接...
[12:03:30] [Godot] Godot 客户端已连接！  ← 这行很重要！
```

### Godot 控制台应该显示：
```
MCP Client 已启动
已连接到 MCP 服务器 127.0.0.1:7777
```

## 🐛 故障排除

### 如果还是不连接：

#### 1. 检查路径是否正确
在 `McpPlugin.cs` 中，确保路径匹配你的实际目录：

```csharp
// 如果你的插件在 addons/GodotPlugin/，改为：
private const string AutoloadPath = "res://addons/GodotPlugin/McpClient.cs";

// 如果在 addons/mcp_client/，保持：
private const string AutoloadPath = "res://addons/mcp_client/McpClient.cs";
```

#### 2. 检查端口是否被占用
```bash
# macOS/Linux
lsof -i :7777

# Windows
netstat -ano | findstr :7777
```

#### 3. 手动测试连接
在 Godot 的主场景中添加一个测试节点：

创建 `TestMcp.cs`：
```csharp
using Godot;

public partial class TestMcp : Node
{
    private McpClient? _client;

    public override void _Ready()
    {
        GD.Print("=== 测试 MCP 连接 ===");
        _client = new McpClient();
        AddChild(_client);
        GD.Print("MCP Client 已手动创建");
    }
}
```

将这个脚本附加到场景中的任何节点，然后运行游戏。

#### 4. 检查防火墙
确保 macOS 防火墙允许本地连接：
- 系统偏好设置 -> 安全性与隐私 -> 防火墙 -> 防火墙选项
- 确保没有阻止 .NET 或 Godot

## 📝 当前你需要做的步骤

1. **复制更新后的插件文件**：
   ```bash
   # 如果你的 Godot 项目路径是（举例）：
   # /Users/arviter/Projects/MyGodotGame
   
   # 复制插件（根据你的实际路径调整）
   cp -r /Users/arviter/Projects/Godot-Mcp/GodotPlugin/* \
         /Users/arviter/Projects/你的Godot项目/addons/mcp_client/
   ```

2. **在 Godot 中重新启用插件**：
   - 禁用插件
   - 重新启用插件
   - 查看输出确认 AutoLoad 已添加

3. **运行游戏（F5）**

4. **观察两个终端**：
   - MCP 服务器终端应显示"Godot 客户端已连接！"
   - Godot 控制台应显示"已连接到 MCP 服务器"

## 🎯 快速检查清单

- [ ] 插件文件在正确的 addons 目录
- [ ] plugin.cfg 中的 script 路径正确
- [ ] 在项目设置中启用了插件
- [ ] AutoLoad 列表中有 McpClientAutoload
- [ ] MCP 服务器正在运行（端口 7777）
- [ ] 游戏已通过 F5 运行（不是在编辑器中测试）
- [ ] 检查 Godot 控制台输出
- [ ] 检查 MCP 服务器终端输出

## 💬 如果问题仍然存在

请提供以下信息：

1. **Godot 项目的完整路径**
2. **插件安装的实际路径**（在 Godot 中）
3. **Godot 控制台的完整输出**
4. **MCP 服务器的完整输出**
5. **项目设置 -> AutoLoad 的截图**

我会根据这些信息进一步诊断问题。
