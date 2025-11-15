# AI Agent 友好的 Godot 调试工具完整指南

## 📋 文档导航

- **[场景树查询工具](./SCENE_QUERY_TOOLS.md)** - 19 种场景树查询方法
- **[信号和日志系统](./SIGNALS_AND_LOGS_GUIDE.md)** - 信号调试 + 增强日志系统
- **[本文档]** - 实战调试场景和 AI Agent 使用建议

---

## 🎯 设计理念

这些工具专为 AI Agent 调试 Godot 游戏设计,考虑到:
- AI 无法准确知道节点的完整路径
- 需要模糊搜索和逐步探索
- 需要了解节点的上下文关系
- 需要控制返回数据量
- **新增**: 简化响应选项 + 信号监听 + 完备日志系统

---

## 🆕 v5.0 新功能

### 简化场景树查询
- `get_scene_tree_simple`: 仅返回名称和类型,减少 90% 响应大小
- 适用场景: AI 快速浏览结构,无需详细属性

### 完整信号系统
- 9 个信号工具: 查询、连接、监听、分析
- 实时信号事件捕获
- 信号连接关系追踪

### 增强日志系统
- 环形缓冲区 (内存最近 1000 条)
- 自动溢出到文件 (每次启动重置)
- 多维度过滤查询 (级别、关键字、时间范围)
- 日志统计和导出

**详细文档**: [SIGNALS_AND_LOGS_GUIDE.md](./SIGNALS_AND_LOGS_GUIDE.md)

---

## 🔍 实际调试场景

### 场景 1: "玩家移动有问题"

**AI Agent 的思路:**
1. 不知道玩家节点叫什么名字,可能是 "Player", "Character", "Hero"
2. 不知道在场景树的哪个位置

**解决方案:**

```http
# 步骤 1: 模糊搜索玩家节点
POST /search_nodes
{
  "namePattern": "player",
  "caseSensitive": false,
  "exactMatch": false,
  "maxResults": 10
}

# 返回:
{
  "count": 2,
  "nodes": [
    { "name": "Player", "type": "CharacterBody2D", "path": "/root/Main/Player" },
    { "name": "PlayerSprite", "type": "Sprite2D", "path": "/root/Main/Player/PlayerSprite" }
  ]
}

# 步骤 2: 获取 Player 节点的上下文
POST /get_node_context
{
  "nodePath": "/root/Main/Player",
  "includeParent": true,
  "includeSiblings": true,
  "includeChildren": true
}

# 返回完整上下文:
{
  "node": { "name": "Player", "type": "CharacterBody2D", "path": "/root/Main/Player" },
  "parent": { "name": "Main", "type": "Node2D", "path": "/root/Main" },
  "siblings": [
    { "name": "Camera", "type": "Camera2D", "path": "/root/Main/Camera" },
    { "name": "TileMap", "type": "TileMap", "path": "/root/Main/TileMap" }
  ],
  "children": [
    { "name": "CollisionShape2D", "type": "CollisionShape2D" },
    { "name": "AnimatedSprite2D", "type": "AnimatedSprite2D" }
  ]
}

# 步骤 3: 查看 Player 的属性
POST /get_property
{
  "nodePath": "/root/Main/Player",
  "propertyName": "position"
}
```

---

### 场景 2: "找不到所有的敌人"

**问题:** 游戏中有多个敌人,可能名字不同,但都属于 "enemies" 组

**解决方案:**

```http
# 方案 1: 按组查找 (最准确)
POST /find_nodes_by_group
{
  "groupName": "enemies",
  "rootPath": "/root"
}

# 返回所有在 "enemies" 组的节点:
{
  "count": 5,
  "nodes": [
    { "name": "Zombie1", "type": "CharacterBody2D", "path": "/root/Main/Enemies/Zombie1", "groups": ["enemies", "ai"] },
    { "name": "Zombie2", "type": "CharacterBody2D", "path": "/root/Main/Enemies/Zombie2", "groups": ["enemies", "ai"] },
    { "name": "Boss", "type": "CharacterBody2D", "path": "/root/Main/Enemies/Boss", "groups": ["enemies", "boss"] }
  ]
}

# 方案 2: 组合搜索 (类型 + 组)
POST /search_nodes
{
  "nodeType": "CharacterBody2D",
  "groupName": "enemies",
  "maxResults": 100
}
```

---

### 场景 3: "相机跟随有问题,不知道相机在哪"

**解决方案:**

```http
# 步骤 1: 按类型查找所有相机
POST /find_nodes_by_type
{
  "nodeType": "Camera2D",
  "rootPath": "/root"
}

# 返回:
{
  "count": 2,
  "nodes": [
    { "name": "MainCamera", "type": "Camera2D", "path": "/root/Main/MainCamera" },
    { "name": "MinimapCamera", "type": "Camera2D", "path": "/root/UI/Minimap/MinimapCamera" }
  ]
}

# 步骤 2: 查看主相机的父级关系 (向上追溯 3 层,包含兄弟节点)
POST /get_node_ancestors
{
  "nodePath": "/root/Main/MainCamera",
  "levels": 3,
  "includeSiblings": true
}

# 返回:
{
  "ancestorCount": 2,
  "ancestors": [
    {
      "level": 1,
      "name": "Main",
      "type": "Node2D",
      "path": "/root/Main",
      "siblings": [
        { "name": "UI", "type": "CanvasLayer" },
        { "name": "AudioManager", "type": "Node" }
      ],
      "siblingCount": 2
    },
    {
      "level": 2,
      "name": "root",
      "type": "Window",
      "path": "/root"
    }
  ]
}
```

---

### 场景 4: "场景太大,不知道从哪开始看"

**解决方案:**

```http
# 步骤 1: 先看统计概览
POST /get_scene_tree_stats
{
  "nodePath": "/root"
}

# 返回:
{
  "totalNodes": 147,
  "maxDepth": 6,
  "nodesByType": {
    "Node2D": 45,
    "Sprite2D": 38,
    "CollisionShape2D": 22,
    "CharacterBody2D": 12,
    "Camera2D": 2
  },
  "nodesByGroup": {
    "enemies": 10,
    "items": 15,
    "players": 1
  }
}

# 步骤 2: 逐层探索 (只看 2 层深度)
POST /get_node_subtree
{
  "nodePath": "/root",
  "maxDepth": 2,
  "includeProperties": false
}

# 步骤 3: 发现感兴趣的分支,继续深入
POST /get_node_subtree
{
  "nodePath": "/root/Main/Enemies",
  "maxDepth": 3,
  "includeProperties": true
}
```

---

## 📋 工具选择决策树

```
需要查找节点?
├─ 知道准确名字? → node_exists
├─ 知道部分名字? → find_nodes_by_name (exactMatch=false)
├─ 知道节点类型? → find_nodes_by_type
├─ 知道节点在哪个组? → find_nodes_by_group
└─ 知道多个条件? → search_nodes (组合查询)

需要了解节点关系?
├─ 查看直接子节点? → get_node_children
├─ 查看父节点? → get_node_parent
├─ 查看父级链? → get_node_ancestors
└─ 查看完整上下文? → get_node_context (父+兄弟+子)

需要探索场景树?
├─ 场景概览? → get_scene_tree_stats
├─ 按深度探索? → get_node_subtree (控制 maxDepth)
└─ 完整树? → get_scene_tree (慎用,可能很大)
```

---

## 🛠️ 高级查询技巧

### 1. **模糊搜索 + 限制结果**

```http
POST /find_nodes_by_name
{
  "namePattern": "enemy",
  "caseSensitive": false,
  "exactMatch": false,
  "maxResults": 20
}
```

**返回:**
```json
{
  "count": 12,
  "nodes": [...],
  "truncated": false  // 如果为 true,说明结果被截断了
}
```

### 2. **组合条件智能搜索**

```http
POST /search_nodes
{
  "namePattern": "player",
  "nodeType": "CharacterBody2D",
  "groupName": "players",
  "maxResults": 5
}
```

**用途:** 当不确定节点特征时,提供多个线索让系统筛选

### 3. **向上追溯父级关系**

```http
POST /get_node_ancestors
{
  "nodePath": "/root/Level1/Area/Enemies/Zombie1",
  "levels": -1,  // -1 = 追溯到根节点
  "includeSiblings": true
}
```

**返回完整父级链:**
```json
{
  "ancestors": [
    {
      "level": 1,
      "name": "Enemies",
      "siblings": ["Player", "Items", "Obstacles"]
    },
    {
      "level": 2,
      "name": "Area",
      "siblings": ["UI", "Camera"]
    },
    {
      "level": 3,
      "name": "Level1",
      "siblings": []
    },
    {
      "level": 4,
      "name": "root"
    }
  ]
}
```

### 4. **节点上下文 (周边环境)**

```http
POST /get_node_context
{
  "nodePath": "/root/Main/Player",
  "includeParent": true,
  "includeSiblings": true,
  "includeChildren": true
}
```

**用途:** 一次性了解节点的完整环境,适合调试节点间交互问题

---

## 🎓 AI Agent 最佳实践

### ✅ 推荐做法

1. **从统计开始**: 先用 `get_scene_tree_stats` 了解全局
2. **模糊搜索**: 使用 `search_nodes` 组合多个条件
3. **逐步深入**: 用 `get_node_subtree` 控制深度,避免一次加载太多
4. **限制结果**: 设置 `maxResults` 避免返回过多数据
5. **查看上下文**: 用 `get_node_context` 了解节点周边环境

### ❌ 避免做法

1. **不要盲目使用 `get_scene_tree`**: 完整树可能有数百个节点
2. **不要精确匹配不存在的路径**: 先用模糊搜索确认路径
3. **不要忽略 `truncated` 字段**: 如果为 true,说明还有更多结果

---

## 📊 性能对比

| 工具 | 返回数据量 | 速度 | 适用场景 |
|------|-----------|------|----------|
| `get_scene_tree_stats` | 极小 | 🟢 极快 | 场景概览 |
| `node_exists` | 极小 | 🟢 极快 | 路径验证 |
| `get_node_children` | 小 | 🟢 快 | 查看直接子节点 |
| `get_node_context` | 小-中 | 🟢 快 | 节点周边环境 |
| `find_nodes_by_group` | 小-中 | 🟡 中 | 按组查找 |
| `find_nodes_by_name` | 中 | 🟡 中 | 模糊搜索 (限制 maxResults) |
| `search_nodes` | 中 | 🟡 中 | 组合条件查询 |
| `get_node_subtree` | 中-大 | 🟡 中 | 深度受控 (maxDepth=1-3) |
| `get_scene_tree` | 大 | 🔴 慢 | 完整树 (慎用) |

---

## 🎯 典型调试工作流

```
1. get_scene_tree_stats("/root")
   → 了解: 共 147 个节点, 12 个 CharacterBody2D, 10 个在 "enemies" 组

2. search_nodes(namePattern="player", nodeType="CharacterBody2D")
   → 找到: /root/Main/Player

3. get_node_context("/root/Main/Player")
   → 了解: 父节点是 Main, 兄弟有 Camera/TileMap, 子节点有 CollisionShape2D/Sprite

4. get_property("/root/Main/Player", "position")
   → 检查: position 为 (100, 200)

5. find_nodes_by_group("enemies")
   → 找到: 10 个敌人节点

6. get_node_ancestors("/root/Main/Enemies/Zombie1", levels=3, includeSiblings=true)
   → 了解: 父级链和每层的兄弟节点

7. 解决问题! 🎉
```

---

## 🚀 总结

新工具解决的核心问题:

1. ✅ **模糊搜索** - AI 不需要知道准确路径
2. ✅ **灵活过滤** - caseSensitive, exactMatch, maxResults
3. ✅ **上下文感知** - 父节点、兄弟节点、子节点一次获取
4. ✅ **组支持** - 按 Godot 的组机制查找节点
5. ✅ **组合查询** - 同时按名称+类型+组搜索
6. ✅ **深度控制** - 避免加载过大的子树
7. ✅ **性能优化** - 统计信息、结果截断

现在 AI Agent 可以像人类开发者一样高效地调试 Godot 游戏! 🎮✨
