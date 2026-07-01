using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// TestHarness — creates a rich node hierarchy and exposes test methods/properties/signals
/// for comprehensive MCP endpoint testing.
/// </summary>
public partial class TestHarness : Node
{
    // ========== Exported properties for MCP testing ==========

    [Export] public string Greeting { get; set; } = "Hello from Godot MCP!";
    [Export] public int Score { get; set; } = 42;
    [Export] public float Speed { get; set; } = 12.5f;
    [Export] public bool IsActive { get; set; } = true;
    [Export] public Godot.Collections.Array<string> Tags { get; set; } = new() { "test", "mcp", "godot" };
    [Export] public Color PlayerColor { get; set; } = Colors.CornflowerBlue;

    // ========== Signals for MCP testing ==========

    [Signal] public delegate void TestSignalEventHandler(string message, int value);
    [Signal] public delegate void PlayerDiedEventHandler();
    [Signal] public delegate void ScoreChangedEventHandler(int newScore);
    [Signal] public delegate void CustomNotificationEventHandler(string notification, float duration);

    // ========== Internal tracking ==========

    private int _counter;
    private readonly List<string> _eventLog = new();
    private Node2D _playerNode;
    private Sprite2D _spriteNode;
    private Label _statusLabel;
    private Godot.Timer _signalEmitter;

    public override void _Ready()
    {
        GD.Print("=".PadRight(60, '='));
        GD.Print("[TestHarness] Godot MCP Test Project v2.0");
        GD.Print("=".PadRight(60, '='));

        // Create a rich test node hierarchy
        CreateTestHierarchy();

        // Setup signal emitter (fires signals periodically)
        SetupSignalEmitter();

        // Connect some signals for testing
        SetupSignalConnections();

        GD.Print("[TestHarness] Ready for MCP testing!");
        GD.Print("[TestHarness] Server should be at http://127.0.0.1:7777/");
    }

    private void CreateTestHierarchy()
    {
        // /root/TestHarness/GameWorld/
        var gameWorld = new Node2D { Name = "GameWorld" };
        AddChild(gameWorld);

        // /root/TestHarness/GameWorld/Player (CharacterBody2D)
        _playerNode = new CharacterBody2D { Name = "Player" };
        _playerNode.Position = new Vector2(400, 300);
        gameWorld.AddChild(_playerNode);

        var collisionShape = new CollisionShape2D { Name = "CollisionShape" };
        collisionShape.Shape = new RectangleShape2D { Size = new Vector2(32, 32) };
        _playerNode.AddChild(collisionShape);

        // /root/TestHarness/GameWorld/Player/Sprite
        _spriteNode = new Sprite2D { Name = "Sprite" };
        _spriteNode.Position = new Vector2(0, 0);
        _playerNode.AddChild(_spriteNode);

        // /root/TestHarness/GameWorld/Enemies/
        var enemies = new Node2D { Name = "Enemies" };
        gameWorld.AddChild(enemies);

        for (int i = 0; i < 3; i++)
        {
            var enemy = new CharacterBody2D { Name = $"Enemy{i + 1}" };
            enemy.Position = new Vector2(200 + i * 200, 200);
            enemies.AddChild(enemy);

            var enemySprite = new Sprite2D { Name = "EnemySprite" };
            enemy.AddChild(enemySprite);

            // Add enemies to group
            enemy.AddToGroup("enemies");
            enemy.AddToGroup($"difficulty_{i switch { 0 => "easy", 1 => "medium", _ => "hard" }}");
        }

        // /root/TestHarness/GameWorld/Items/
        var items = new Node2D { Name = "Items" };
        gameWorld.AddChild(items);

        string[] itemNames = { "HealthPotion", "ManaPotion", "Key", "Gem", "Coin5" };
        foreach (var name in itemNames)
        {
            var item = new Area2D { Name = name };
            item.Position = new Vector2(100 + Array.IndexOf(itemNames, name) * 150, 500);
            items.AddChild(item);

            var itemSprite = new Sprite2D { Name = "Icon" };
            item.AddChild(itemSprite);

            var collision = new CollisionShape2D { Name = "PickupArea" };
            collision.Shape = new RectangleShape2D { Size = new Vector2(16, 16) };
            item.AddChild(collision);

            item.AddToGroup("items");
            item.AddToGroup("pickups");
        }

        // /root/TestHarness/UI/
        var ui = new CanvasLayer { Name = "UI" };
        AddChild(ui);

        _statusLabel = new Label { Name = "StatusLabel" };
        _statusLabel.Text = "MCP Test Running...";
        _statusLabel.Position = new Vector2(10, 10);
        _statusLabel.AddThemeColorOverride("font_color", Colors.White);
        ui.AddChild(_statusLabel);

        var scoreLabel = new Label { Name = "ScoreLabel" };
        scoreLabel.Text = $"Score: {Score}";
        scoreLabel.Position = new Vector2(10, 40);
        scoreLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        ui.AddChild(scoreLabel);

        // /root/TestHarness/Camera
        var camera = new Camera2D { Name = "Camera" };
        AddChild(camera);

        // Add some nodes to groups
        AddToGroup("test_nodes");
        gameWorld.AddToGroup("game_systems");
        ui.AddToGroup("game_systems");
    }

    private void SetupSignalEmitter()
    {
        _signalEmitter = new Godot.Timer { Name = "SignalEmitter" };
        _signalEmitter.WaitTime = 5.0f;
        _signalEmitter.Autostart = true;
        _signalEmitter.OneShot = false;
        AddChild(_signalEmitter);
        _signalEmitter.Timeout += OnSignalEmitterTimeout;
    }

    private void SetupSignalConnections()
    {
        // Signal connections handled via MCP tools for testing
        GD.Print("[TestHarness] Signal connections ready for MCP testing");
    }

    private void OnSignalEmitterTimeout()
    {
        _counter++;
        GD.Print($"[TestHarness] Timer heartbeat #{_counter}");
    }

    public override void _Process(double delta)
    {
        // Update status label
        if (_statusLabel != null)
        {
            _statusLabel.Text = $"MCP Running | FPS: {Engine.GetFramesPerSecond()} | Nodes: {GetTree().Root.GetChildCount()}";
        }
    }

    // ========== Public methods for MCP tool testing ==========

    public int AddNumbers(int a, int b)
    {
        int result = a + b;
        GD.Print($"[TestHarness] AddNumbers({a}, {b}) = {result}");
        return result;
    }

    public void ResetScore()
    {
        Score = 0;
        GD.Print("[TestHarness] Score reset to 0");
    }

    public string GetStatus()
    {
        return $"Active={IsActive}, Score={Score}, Speed={Speed:F1}, Counter={_counter}";
    }

    public Dictionary<string, object> GetStats()
    {
        return new Dictionary<string, object>
        {
            ["counter"] = _counter,
            ["score"] = Score,
            ["isActive"] = IsActive,
            ["nodeCount"] = GetTree().GetNodeCount(),
            ["eventLogSize"] = _eventLog.Count
        };
    }

    public void LogEvent(string eventName)
    {
        _eventLog.Add($"[{DateTime.Now:HH:mm:ss}] {eventName}");
        GD.Print($"[TestHarness] Event logged: {eventName}");
    }

    public string[] GetEventLog()
    {
        return _eventLog.ToArray();
    }

    public void TeleportPlayer(float x, float y)
    {
        if (_playerNode != null)
        {
            _playerNode.Position = new Vector2(x, y);
            EmitSignal(SignalName.CustomNotification, $"Player teleported to ({x}, {y})", 2.0f);
        }
    }

    public void EmitScoreChanged(int newScore)
    {
        EmitSignal(SignalName.ScoreChanged, newScore);
    }
}
