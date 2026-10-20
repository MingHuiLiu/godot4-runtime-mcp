# 扩展场景树查询工具

## 📋 问题背景

完整的场景树 (`get_scene_tree`) 可能会非常大,导致:
- JSON 响应体积过大
- 网络传输慢
- AI Agent 处理困难
- 查找特定节点效率低

## 🎯 解决方案

新增 **7 个细粒度场景树查询方法**,让 AI Agent 能够高效定位和排查问题。

---

## 🔧 新增 MCP 工具

### 1. `get_node_children` - 获取直接子节点

**用途**: 轻量级查询,只返回一层子节点,不递归

**参数**:
- `nodePath`: 节点路径

**返回示例**:
```json
{
  "success": true,
  "data": [
    {
      "name": "Player",
      "type": "CharacterBody2D",
      "path": "/root/Main/Player"
    },
    {
      "name": "Camera",
      "type": "Camera2D",
      "path": "/root/Main/Camera"
    }
  ]
}
```

**使用场景**:
- 快速查看某个节点下有哪些子节点
- 避免加载整个子树

---

### 2. `get_node_parent` - 获取父节点

**用途**: 查找节点的父级

**参数**:
- `nodePath`: 节点路径

**返回示例**:
```json
{
  "success": true,
  "data": {
    "name": "Main",
    "type": "Node2D",
    "path": "/root/Main"
  }
}
```

**使用场景**:
- 向上追溯节点层级关系
- 验证节点的父子关系

---

### 3. `find_nodes_by_type` - 按类型查找节点

**用途**: 在整个场景树中查找指定类型的所有节点

**参数**:
- `nodeType`: 节点类型 (如 "Sprite2D", "Camera2D")
- `rootPath`: 搜索起始路径 (默认 "/root")

**返回示例**:
```json
{
  "success": true,
  "data": {
    "count": 3,
    "nodes": [
      {
        "name": "PlayerSprite",
        "type": "Sprite2D",
        "path": "/root/Main/Player/PlayerSprite"
      },
      {
        "name": "EnemySprite",
        "type": "Sprite2D",
        "path": "/root/Main/Enemies/Enemy1/EnemySprite"
      },
      {
        "name": "ItemSprite",
        "type": "Sprite2D",
        "path": "/root/Main/Items/Coin/ItemSprite"
      }
    ]
  }
}
```

**使用场景**:
- 查找所有 Camera2D 节点
- 列出所有 CollisionShape2D
- 定位特定功能的节点

---

### 4. `find_nodes_by_name` - 按名称搜索节点

**用途**: 模糊匹配节点名称,支持部分匹配

**参数**:
- `namePattern`: 名称模式 (支持部分匹配,不区分大小写)
- `rootPath`: 搜索起始路径 (默认 "/root")

**返回示例**:
```json
{
  "success": true,
  "data": {
    "count": 2,
    "nodes": [
      {
        "name": "Player",
        "type": "CharacterBody2D",
        "path": "/root/Main/Player"
      },
      {
        "name": "PlayerSprite",
        "type": "Sprite2D",
        "path": "/root/Main/Player/PlayerSprite"
      }
    ]
  }
}
```

**使用场景**:
- 不记得完整节点路径,只知道名称
- 查找所有包含 "Enemy" 的节点
- 快速定位问题节点

---

### 5. `get_scene_tree_stats` - 获取场景树统计

**用途**: 获取场景树的概览信息,包括节点总数和类型分布

**参数**:
- `nodePath`: 统计根路径 (默认 "/root")

**返回示例**:
```json
{
  "success": true,
  "data": {
    "totalNodes": 47,
    "nodesByType": {
      "Node2D": 15,
      "Sprite2D": 12,
      "CollisionShape2D": 8,
      "Camera2D": 2,
      "CharacterBody2D": 5,
      "StaticBody2D": 3,
      "Label": 2
    },
    "rootPath": "/root"
  }
}
```

**使用场景**:
- 快速了解场景规模
- 检查节点类型分布是否合理
- 性能调优前的基础数据

---

### 6. `node_exists` - 检查节点是否存在

**用途**: 快速验证节点路径是否有效

**参数**:
- `nodePath`: 节点路径

**返回示例**:
```json
{
  "success": true,
  "data": {
    "exists": true,
    "path": "/root/Main/Player"
  }
}
```

**使用场景**:
- 在操作节点前验证路径
- 调试节点路径错误
- 条件判断

---

### 7. `get_node_subtree` - 获取指定深度的子树

**用途**: 按深度控制的树形结构查询,避免完整树太大

**参数**:
- `nodePath`: 根节点路径
- `maxDepth`: 最大递归深度
  - `0`: 仅当前节点信息
  - `1`: 包含直接子节点
  - `2`: 包含孙节点
  - `-1`: 无限深度 (完整子树)

**返回示例** (maxDepth=2):
```json
{
  "success": true,
  "data": {
    "name": "Main",
    "type": "Node2D",
    "path": "/root/Main",
    "depth": 0,
    "children": [
      {
        "name": "Player",
        "type": "CharacterBody2D",
        "path": "/root/Main/Player",
        "depth": 1,
        "children": [
          {
            "name": "Sprite",
            "type": "Sprite2D",
            "path": "/root/Main/Player/Sprite",
            "depth": 2,
            "children": [],
            "hasChildren": false
          }
        ]
      }
    ]
  }
}
```

**使用场景**:
- 逐层探索场景树
- 控制响应大小
- 按需加载子树

---

## 🚀 使用建议

### AI Agent 调试工作流

1. **先用 `get_scene_tree_stats` 了解全局**
   - 查看场景规模
   - 了解节点类型分布

2. **用 `find_nodes_by_type` 或 `find_nodes_by_name` 定位目标**
   - 找到问题相关的节点路径

3. **用 `get_node_info` 查看详细信息**
   - 获取属性、方法列表

4. **用 `get_node_children` 查看子节点**
   - 快速了解节点结构

5. **用 `get_node_subtree` 获取局部树**
   - 深入探索特定分支

### 性能优化建议

| 方法 | 性能 | 适用场景 |
|------|------|----------|
| `get_scene_tree_stats` | 🟢 快 | 场景概览 |
| `node_exists` | 🟢 快 | 路径验证 |
| `get_node_children` | 🟢 快 | 浅层查询 |
| `get_node_parent` | 🟢 快 | 父节点查询 |
| `find_nodes_by_type` | 🟡 中 | 类型搜索 (递归) |
| `find_nodes_by_name` | 🟡 中 | 名称搜索 (递归) |
| `get_node_subtree` | 🟡 中 | 深度受控 |
| `get_scene_tree` | 🔴 慢 | 完整树 (慎用) |

---

## 📝 HTTP 测试

使用 `test-godot-api.http` 文件测试所有新方法:

```http
# 查找所有 Camera2D
POST http://127.0.0.1:7777/find_nodes_by_type
Content-Type: application/json

{
  "nodeType": "Camera2D",
  "rootPath": "/root"
}

# 获取场景统计
POST http://127.0.0.1:7777/get_scene_tree_stats
Content-Type: application/json

{
  "nodePath": "/root"
}

# 获取 2 层深度的子树
POST http://127.0.0.1:7777/get_node_subtree
Content-Type: application/json

{
  "nodePath": "/root",
  "maxDepth": 2
}
```

---

## 🎯 总结

新增的 7 个方法解决了以下问题:

1. ✅ **避免完整树过大** - 提供细粒度查询
2. ✅ **提高查询效率** - 精确定位目标节点
3. ✅ **更好的调试体验** - AI Agent 可以逐步探索
4. ✅ **降低网络负载** - 按需获取数据
5. ✅ **场景树分析** - 统计信息帮助优化

现在 AI Agent 可以像文件系统一样高效地浏览和操作 Godot 场景树! 🎮
