# Godot MCP HTTP API 架构说明

## 🔧 通信架构

### **端点设计**
- **Godot HTTP 服务器**: `http://127.0.0.1:7777/`
- **MCP 服务器**: HTTP 客户端

### **请求格式**
```http
POST http://127.0.0.1:7777/
Content-Type: application/json

{
  "method": "get_scene_tree",
  "parameters": {
    "param1": "value1"
  }
}
```

### **响应格式**
```json
{
  "Success": true,
  "Data": { ... },
  "Error": null
}
```

## 📋 可用方法

### 场景管理
- `get_scene_tree` - 获取场景树
- `get_node_info` - 获取节点信息 (参数: `nodePath`)
- `create_node` - 创建节点 (参数: `parentPath`, `nodeType`, `nodeName`)
- `delete_node` - 删除节点 (参数: `nodePath`)
- `load_scene` - 加载场景 (参数: `scenePath`)

### 属性操作
- `get_property` - 获取属性 (参数: `nodePath`, `propertyName`)
- `set_property` - 设置属性 (参数: `nodePath`, `propertyName`, `value`)
- `list_properties` - 列出属性 (参数: `nodePath`)

### 方法调用
- `call_method` - 调用方法 (参数: `nodePath`, `methodName`, `args`)
- `list_methods` - 列出方法 (参数: `nodePath`)

### 脚本执行
- `execute_csharp` - 执行 C# 代码 (参数: `code`)
- `get_global_variables` - 获取全局变量

### 资源管理
- `list_resources` - 列出资源 (参数: `path`)
- `load_resource` - 加载资源 (参数: `resourcePath`)
- `get_resource_info` - 获取资源信息 (参数: `resourcePath`)

### 调试工具
- `get_logs` - 获取日志 (参数: `level`)
- `get_performance_stats` - 获取性能统计
- `take_screenshot` - 截图 (参数: `savePath`)

## 🧪 测试示例

### 使用 curl 测试
```bash
# 获取场景树
curl -X POST http://127.0.0.1:7777/ \
  -H "Content-Type: application/json" \
  -d '{"method":"get_scene_tree","parameters":{}}'

# 获取节点信息
curl -X POST http://127.0.0.1:7777/ \
  -H "Content-Type: application/json" \
  -d '{"method":"get_node_info","parameters":{"nodePath":"/root"}}'
```

## 📊 日志格式

### Godot 控制台
```
============================================================
[MCP] Godot MCP HTTP API 服务器
[MCP] 版本: 2.0 (HTTP REST API)
============================================================
[MCP] ✓ HTTP API 服务器已成功启动
[MCP] ✓ 监听地址: http://127.0.0.1:7777/
[MCP] 等待新的 HTTP 请求...
[MCP] 收到请求: POST /
[MCP:a1b2c3d4] 调用方法: get_scene_tree
[MCP] 方法 get_scene_tree 执行成功: True
[MCP:a1b2c3d4] ✓ 请求处理完成
```

### MCP 服务器日志
```
调用 Godot 方法: get_scene_tree
请求内容: {"method":"get_scene_tree","parameters":{}}
HTTP 状态码: 200 OK
✓ 调用成功: get_scene_tree
```

## 🔍 故障排查

### 错误: "未知方法"
- **原因**: 请求体中的 `method` 字段为空或拼写错误
- **解决**: 检查请求 JSON 格式,确保 `method` 字段正确

### 错误: "未连接到 Godot"
- **原因**: Godot 游戏未运行或 HTTP 服务器未启动
- **解决**: 
  1. 启动 Godot 游戏
  2. 检查控制台是否有 "[MCP] ✓ HTTP API 服务器已成功启动" 消息
  3. 确认端口 7777 未被占用

### 错误: "HTTP 404"
- **原因**: URL 路径错误
- **解决**: 确保请求发送到 `http://127.0.0.1:7777/` (根路径)

## ✅ 测试检查清单

- [ ] Godot 游戏已启动
- [ ] 控制台显示 "[MCP] ✓ HTTP API 服务器已成功启动"
- [ ] MCP 服务器已启动 (VSCode 或 dotnet run)
- [ ] 在 Copilot 中可以看到 18 个工具
- [ ] 调用工具时 Godot 控制台有日志输出
- [ ] 工具返回正确的响应
