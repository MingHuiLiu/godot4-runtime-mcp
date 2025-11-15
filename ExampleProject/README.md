# 示例 Godot 项目

这个目录包含一个简单的 Godot 4 示例项目，用于演示 MCP 插件的使用。

## 项目结构

```
ExampleProject/
├── project.godot          # Godot 项目配置
├── addons/
│   └── mcp_client/        # MCP 插件（从上级目录复制）
├── scenes/
│   ├── main.tscn          # 主场景
│   └── player.tscn        # 玩家场景
└── scripts/
    └── Player.cs          # 玩家脚本
```

## 如何使用

### 1. 创建 Godot 项目

在 Godot 编辑器中：
1. 新建项目
2. 选择 C# 作为脚本语言
3. 保存项目

### 2. 安装 MCP 插件

```bash
# 从项目根目录
cp -r GodotPlugin ExampleProject/addons/mcp_client
```

或者手动复制 `GodotPlugin` 文件夹到 `ExampleProject/addons/mcp_client`

### 3. 启用插件

1. 在 Godot 编辑器中打开项目
2. 项目 -> 项目设置 -> 插件
3. 启用 "MCP Client"

### 4. 创建测试场景

#### 主场景 (main.tscn)

创建节点结构：
```
Main (Node2D)
├── Player (CharacterBody2D)
│   └── Sprite2D
└── Camera2D
```

#### 玩家脚本 (Player.cs)

```csharp
using Godot;

public partial class Player : CharacterBody2D
{
    [Export]
    public float Speed { get; set; } = 200.0f;
    
    [Export]
    public int Health { get; set; } = 100;

    public override void _PhysicsProcess(double delta)
    {
        Vector2 velocity = Velocity;
        Vector2 direction = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        
        if (direction != Vector2.Zero)
        {
            velocity = direction * Speed;
        }
        else
        {
            velocity = Vector2.Zero;
        }

        Velocity = velocity;
        MoveAndSlide();
    }
    
    public void TakeDamage(int damage)
    {
        Health -= damage;
        GD.Print($"Player took {damage} damage. Health: {Health}");
        
        if (Health <= 0)
        {
            Die();
        }
    }
    
    public void Heal(int amount)
    {
        Health += amount;
        GD.Print($"Player healed {amount}. Health: {Health}");
    }
    
    private void Die()
    {
        GD.Print("Player died!");
        // 可以在这里添加死亡逻辑
    }
}
```

### 5. 运行和测试

1. 启动 MCP 服务器
```bash
./start-server.sh
```

2. 在 Godot 中运行游戏 (F5)

3. 在 Claude 中测试 MCP 工具

## 测试用例

### 基础操作

1. **查看场景树**
```
获取当前场景树结构
```

2. **查看玩家信息**
```
获取节点 /root/Main/Player 的详细信息
```

3. **查看玩家属性**
```
获取 /root/Main/Player 的 Speed 和 Health 属性
```

### 修改操作

4. **修改速度**
```
将玩家的 Speed 属性设置为 500
```

5. **修改位置**
```
将玩家位置设置为 (400, 300)
```

6. **调用方法**
```
调用 /root/Main/Player 的 TakeDamage 方法，参数为 [20]
```

### 高级操作

7. **创建新节点**
```
在 /root/Main 下创建一个名为 "Enemy" 的 CharacterBody2D 节点
```

8. **性能监控**
```
获取当前的性能统计信息
```

9. **截图**
```
截取当前游戏画面并保存
```

## 预期结果

### 场景树输出
```json
{
  "path": "/root",
  "name": "root",
  "type": "Window",
  "children": [
    {
      "path": "/root/Main",
      "name": "Main",
      "type": "Node2D",
      "children": [
        {
          "path": "/root/Main/Player",
          "name": "Player",
          "type": "CharacterBody2D",
          "children": [...]
        }
      ]
    }
  ]
}
```

### 节点信息输出
```json
{
  "path": "/root/Main/Player",
  "name": "Player",
  "type": "CharacterBody2D",
  "properties": {
    "position": {"x": 0, "y": 0},
    "Speed": 200,
    "Health": 100
  },
  "methods": ["TakeDamage", "Heal", "_PhysicsProcess", ...],
  "signals": [...]
}
```

## 故障排除

### 插件未加载
- 检查插件文件是否在正确位置
- 确保 plugin.cfg 格式正确
- 重新启动 Godot 编辑器

### 无法连接到 MCP 服务器
- 确认 MCP 服务器正在运行
- 检查端口 7777 未被占用
- 查看 Godot 控制台错误信息

### 节点路径错误
- 使用 get_scene_tree 确认正确的节点路径
- 节点路径区分大小写
- 确保使用绝对路径（以 /root 开头）

## 扩展练习

1. **创建多个敌人**
   - 使用 create_node 创建 5-10 个敌人
   - 为每个敌人设置随机位置

2. **性能优化**
   - 监控 FPS
   - 使用 get_scene_tree 找出节点数量
   - 尝试删除不必要的节点

3. **游戏平衡调整**
   - 动态调整玩家速度
   - 测试不同的 Health 值
   - 实时调整游戏难度

4. **调试工作流**
   - 在游戏运行时修改变量
   - 测试边界情况
   - 验证游戏逻辑

## 下一步

- 创建更复杂的场景
- 添加更多游戏逻辑
- 使用 MCP 工具进行 AI 辅助开发
- 探索自动化测试可能性
