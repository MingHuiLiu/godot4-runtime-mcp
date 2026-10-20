using Godot;
using GodotRuntimeMcp.Recording;
using System;
using System.Linq;

public partial class SnapshotCaptureProbe : Node
{
    public override void _Ready()
    {
        try
        {
            var game = new Node2D { Name = "GameWorld" };
            AddChild(game);
            var player = new CharacterBody2D { Name = "Player" };
            game.AddChild(player);
            var collision = new CollisionShape2D { Name = "CollisionShape" };
            collision.Shape = new RectangleShape2D { Size = new Vector2(32, 32) };
            player.AddChild(collision);
            var enemy = new Node2D { Name = "Enemy" };
            game.AddChild(enemy);
            int entered = 0, exited = 0;
            enemy.TreeEntered += () => entered++;
            enemy.TreeExited += () => exited++;
            Node? ownerBefore = enemy.Owner;
            Node parentBefore = enemy.GetParent();
            var first = SceneCheckpointCapture.Capture(game, "cp1", "session", "project", "instance", true);
            if (first.Nodes.Count != 4 ||
                first.Nodes.All(n => n.Name != "CollisionShape") ||
                enemy.Owner != ownerBefore || enemy.GetParent() != parentBefore ||
                entered != 0 || exited != 0)
                throw new Exception("Owner-null tree capture missed or mutated nodes");

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
        }
        catch (Exception e)
        {
            GD.PushError("SNAPSHOT_PROBE_FAIL: " + e);
            GetTree().Quit(1);
            return;
        }
        GetTree().Quit(0);
    }
}
