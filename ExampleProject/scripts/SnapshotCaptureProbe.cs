using Godot;
using GodotRuntimeMcp.Recording;
using System;
using System.Linq;

public partial class SnapshotCaptureProbe : Node
{
    private CaptureJobController? _controller;
    private string? _captureJobId;
    private int _capturePollFrames;
    public override void _Ready()
    {
        try
        {
            var game = new Node2D { Name = "GameWorld" };
            AddChild(game);
            var player = new CharacterBody2D { Name = "Player" };
            game.AddChild(player);
            player.Position = new Vector2(100, 200);
            var collision = new CollisionShape2D { Name = "CollisionShape" };
            collision.Shape = new RectangleShape2D { Size = new Vector2(32, 32) };
            player.AddChild(collision);
            var collisionTwin = new CollisionShape2D { Name = "CollisionTwin", Shape = collision.Shape };
            player.AddChild(collisionTwin);
            var enemy = new Node2D { Name = "Enemy" };
            game.AddChild(enemy);
            var propsNode = new SnapshotPropertyProbe { Name = "PropertyProbe" };
            game.AddChild(propsNode);
            var r1 = new SnapshotGraphResource();
            var r2 = new SnapshotGraphResource();
            r1.Next = r2; r2.Next = r1;
            propsNode.CustomResource = r1;
            int entered = 0, exited = 0;
            enemy.TreeEntered += () => entered++;
            enemy.TreeExited += () => exited++;
            Node? ownerBefore = enemy.Owner;
            Node parentBefore = enemy.GetParent();
            var first = SceneCheckpointCapture.Capture(game, "cp1", "session", "project", "instance", true);
            if (first.Nodes.Count != 6 ||
                first.Nodes.All(n => n.Name != "CollisionShape") ||
                !first.Nodes.Single(n => n.Name == "Player").Properties.ContainsKey("position") ||
                first.Nodes.Single(n => n.Name == "Player").Properties["position"].Data?["x"]?.GetValue<float>() != 100 ||
                !first.Nodes.Single(n => n.Name == "PropertyProbe").Properties.TryGetValue("Tags", out var tags) ||
                tags.Type != "array" ||
                !first.Nodes.Single(n => n.Name == "PropertyProbe").Properties.TryGetValue("Counts", out var counts) ||
                counts.Type != "dict" ||
                first.Nodes.Single(n => n.Name == "PropertyProbe").Properties["Vertices"].Type != "packed_array" ||
                first.Resources.Count != 3 ||
                first.Resources.Count(r => r.ScriptPath == "res://scripts/SnapshotGraphResource.cs") != 2 ||
                first.Resources.All(r => r.Properties.ContainsKey("Next") == false) ||
                first.Nodes.Single(n => n.Name == "CollisionShape").Properties["shape"].RefId !=
                  first.Nodes.Single(n => n.Name == "CollisionTwin").Properties["shape"].RefId ||

                enemy.Owner != ownerBefore || enemy.GetParent() != parentBefore ||
                entered != 0 || exited != 0)
                throw new Exception("Owner-null tree capture missed or mutated nodes");

            propsNode.BigText = new string('x', 300_000);
            bool rejected = false;
            try { SceneCheckpointCapture.Capture(game, "bad", "session", "project", "instance", true); }
            catch (System.IO.InvalidDataException e) { rejected = e.Message.Contains("capture_limit_exceeded"); }
            if (!rejected) throw new Exception("Strict capture accepted oversized property");
            propsNode.BigText = "";
            game.RemoveChild(enemy);
            enemy.QueueFree();
            var replacement = new Node2D { Name = "Enemy" };
            game.AddChild(replacement);
            var second = SceneCheckpointCapture.Capture(game, "cp2", "session", "project", "instance", true);
            string firstEnemyId = first.Nodes.Single(n => n.Name == "Enemy").NodeId;
            string newEnemyId = second.Nodes.Single(n => n.Name == "Enemy").NodeId;
            if (firstEnemyId == newEnemyId)
                throw new Exception("Same node path reused snapshot node identity");
            GD.Print("SNAPSHOT_PROBE_PASS: owner-null topology, no live tree mutation, fresh node identity");
            _controller = new CaptureJobController(this);
            var started = _controller.Start(game.GetPath().ToString(), true, 2000);
            if (started.Status != "pending") throw new Exception("Capture did not queue asynchronously");
            var busy = _controller.Start(game.GetPath().ToString(), true, 2000);
            if (busy.Code != "snapshot_busy") throw new Exception("Concurrent capture was not rejected");
            _captureJobId = started.JobId;
        }
        catch (Exception e)
        {
            GD.PushError("SNAPSHOT_PROBE_FAIL: " + e);
            GetTree().Quit(1);
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (_controller is null || _captureJobId is null) return;
        _controller.Pump();
        var status = _controller.GetStatus(_captureJobId);
        if (status.Status == "failed")
        {
            GD.PushError("SNAPSHOT_JOB_FAIL: " + status.Code + ":" + status.Message);
            GetTree().Quit(1);
        }
        if (status.Status == "completed")
        {
            string root = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
            if (System.IO.Directory.GetFiles(root, "manifest.json", System.IO.SearchOption.AllDirectories).Length == 0)
                throw new Exception("Completed capture lacks committed manifest");
            GD.Print("SNAPSHOT_JOB_PASS: async capture persisted and returned completed status");
            GetTree().Quit(0);
        }
        if (++_capturePollFrames > 240)
        {
            GD.PushError("SNAPSHOT_JOB_FAIL: capture timed out");
            GetTree().Quit(1);
        }
    }
}
