using Godot;
using GodotRuntimeMcp.Recording;
using System;
using System.Linq;

public partial class SnapshotCaptureProbe : Node
{
    private CaptureJobController? _controller;
    private string? _captureJobId;
    private int _capturePollFrames;
    private Node2D? _testWorld;
    public override void _Ready()
    {
        try
        {
            var game = new Node2D { Name = "GameWorld" };
            AddChild(game);
            _testWorld = game;
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
            propsNode.AddToGroup("snapshot_team", persistent: true);
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
                !first.Nodes.Single(n => n.Name == "Player").DiagnosticProperties.TryGetValue("global_position", out var livePosition) ||
                livePosition.Data?["x"]?.GetValue<float>() != 100 ||
                !first.Nodes.Single(n => n.Name == "Player").DiagnosticProperties.ContainsKey("velocity") ||
                first.Nodes.Single(n => n.Name == "Player").Properties["position"].Data?["x"]?.GetValue<float>() != 100 ||
                !first.Nodes.Single(n => n.Name == "PropertyProbe").Properties.TryGetValue("Tags", out var tags) ||
                tags.Type != "array" ||
                first.Nodes.Single(n => n.Name == "PropertyProbe").Properties["GridCell"].Type != "vector2i" ||
                first.Nodes.Single(n => n.Name == "PropertyProbe").Properties["Region"].Type != "rect2i" ||
                first.Nodes.Single(n => n.Name == "PropertyProbe").Properties["ProjectionMat"].Type != "projection" ||
                first.Nodes.Single(n => n.Name == "PropertyProbe").Properties["Corners"].Type != "packed_array" ||
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

            // Negative integration fixture: in-memory Resource cycles must not masquerade
            // as a strictly restorable .tscn after Godot's native serializer loses an edge.
            string fixtureRoot = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
            System.Threading.Tasks.Task.Run(() => CheckpointArchive.CommitAsync(
                System.IO.Path.Combine(fixtureRoot, "fixtures", "cyclic_snapshot"),
                first, System.Threading.CancellationToken.None)).GetAwaiter().GetResult();
            var rebuilt = SnapshotSceneBuilder.Build(first, true);
            try
            {
                var worldCopy = rebuilt.Root;
                var copyPlayer = worldCopy.GetNode<CharacterBody2D>("Player");
                var copyA = copyPlayer.GetNode<CollisionShape2D>("CollisionShape");
                var copyB = copyPlayer.GetNode<CollisionShape2D>("CollisionTwin");
                var copyProps = worldCopy.GetNode<SnapshotPropertyProbe>("PropertyProbe");
                if (worldCopy.GetChildCount() != 3 || copyPlayer.Position != new Vector2(100,200) ||
                    !ReferenceEquals(copyA.Shape, copyB.Shape) ||
                    copyProps.GridCell != new Vector2I(12, 18) ||
                    copyProps.Region != new Rect2I(2, 4, 12, 18) ||
                    copyProps.ProjectionMat != Projection.Identity ||
                    copyProps.Corners.Length != 1 || copyProps.Corners[0] != new Vector4(1, 2, 3, 4) ||
                    copyProps.Tags.Count != 2 || copyProps.Counts["beta"] != 2 ||
                    copyProps.CustomResource is not SnapshotGraphResource copyR1 ||
                    copyR1.Next is not SnapshotGraphResource copyR2 ||
                    !ReferenceEquals(copyR2.Next, copyR1))
                    throw new Exception("Restored scene lost structure, properties, or shared resources");
                if (copyPlayer == player || copyA == collision || copyProps == propsNode)
                    throw new Exception("Scene restore reused live nodes");
                GD.Print("SNAPSHOT_REBUILD_PASS: independent tree and cyclic resource graph");
            }
            finally
            {
                // Cyclic RefCounted resources outlive freed Nodes unless the cycle
                // is explicitly broken; finalizers must run while Godot is alive.
                var copyProps = rebuilt.Root.GetNode<SnapshotPropertyProbe>("PropertyProbe");
                var firstCopy = copyProps.CustomResource as SnapshotGraphResource;
                var secondCopy = firstCopy?.Next as SnapshotGraphResource;
                if (firstCopy is not null) firstCopy.Next = null;
                if (secondCopy is not null) secondCopy.Next = null;
                copyProps.CustomResource = null;
                rebuilt.Root.Free();
                secondCopy?.Dispose();
                firstCopy?.Dispose();
            }
            var missingResource = first with { Resources = first.Resources.Skip(1).ToList() };
            bool missingRejected = false;
            try { var partial = SnapshotSceneBuilder.Build(missingResource, true); partial.Root.Free(); }
            catch (System.IO.InvalidDataException) { missingRejected = true; }
            if (!missingRejected) throw new Exception("Strict scene restoration accepted missing resource");
            // Test uncovered native Variant families: strict recording must not silently drop them.
            var variants = new (Variant Value, string Type)[]
            {
                (Variant.From(new Vector2I(7, 9)), "vector2i"),
                (Variant.From(new Vector3I(1, 2, 3)), "vector3i"),
                (Variant.From(new Vector4I(2, 3, 4, 5)), "vector4i"),
                (Variant.From(new Rect2I(1, 2, 3, 4)), "rect2i"),
                (Variant.From(new Plane(new Vector3(0, 1, 0), 12)), "plane"),
                (Variant.From(Projection.Identity), "projection"),
                (Variant.From(new Vector4[] { new(1, 2, 3, 4) }), "packed_array"),
            };
            foreach (var (input, type) in variants)
            {
                using (input)
                {
                    var encoded = VariantSnapshotCodec.Encode(input,
                        new SnapshotEncodeContext(), "n_test", type);
                    if (encoded?.Type != type)
                        throw new Exception($"Missing Variant support for {type}: {encoded?.Type}");
                }
            }
            using (var invalidPosition = Variant.From(new Vector2(float.NaN, 4)))
            {
                var context = new SnapshotEncodeContext();
                var value = VariantSnapshotCodec.Encode(invalidPosition, context, "n", "position");
                if (value is not null || context.Issues.All(i => i.Code != "non_finite_variant"))
                    throw new Exception("Non-finite Vector2 was not rejected with a diagnostic");
            }
            var byteBudget = new SnapshotEncodeContext { MaxTotalBytes = 100 };
            using (var data1 = Variant.From(new string('a', 70)))
            using (var data2 = Variant.From(new string('b', 70)))
            {
                if (VariantSnapshotCodec.Encode(data1, byteBudget, "n", "data1") is null ||
                    VariantSnapshotCodec.Encode(data2, byteBudget, "n", "data2") is not null ||
                    byteBudget.Issues.All(i => i.Code != "capture_limit_exceeded"))
                    throw new Exception("Cumulative property byte budget was not enforced");
            }
            var resourceBudget = new SnapshotEncodeContext { MaxResources = 1 };
            using (var resource1 = new RectangleShape2D())
            using (var resource2 = new RectangleShape2D())
            using (var shape1 = Variant.From(resource1))
            using (var shape2 = Variant.From(resource2))
            {
                if (VariantSnapshotCodec.Encode(shape1, resourceBudget, "n", "one") is null ||
                    VariantSnapshotCodec.Encode(shape2, resourceBudget, "n", "two") is not null ||
                    resourceBudget.Issues.All(i => i.Code != "capture_limit_exceeded"))
                    throw new Exception("Maximum Resource count was not enforced");
            }
            using (var nan = Variant.From(double.NaN))
            {
                var nanContext = new SnapshotEncodeContext();
                var encoded = VariantSnapshotCodec.Encode(nan, nanContext, "n_test", "nonfinite");
                if (encoded is not null || nanContext.Issues.All(i => i.Code != "non_finite_variant"))
                    throw new Exception("Non-finite float was silently recorded as a valid JSON number");
            }
            propsNode.BigText = new string('x', 300_000);
            bool rejected = false;
            try { SceneCheckpointCapture.Capture(game, "bad", "session", "project", "instance", true); }
            catch (System.IO.InvalidDataException e) { rejected = e.Message.Contains("capture_limit_exceeded"); }
            if (!rejected) throw new Exception("Strict capture accepted oversized property");
            propsNode.BigText = "";
            // The snapshot must pin external disk dependencies, not silently accept
            // a different .tres file when reconstructing later in another process.
            string resourcePath = "user://snapshot-external-" + Guid.NewGuid().ToString("N") + ".tres";
            string diskPath = ProjectSettings.GlobalizePath(resourcePath);
            RectangleShape2D? externalLoaded = null;
            try
            {
                var externalShape = new RectangleShape2D { Size = new Vector2(21, 33) };
                if (ResourceSaver.Save(externalShape, resourcePath) != Error.Ok)
                    throw new Exception("Could not write external resource test fixture");
                externalShape.Dispose();
                externalLoaded = ResourceLoader.Load<RectangleShape2D>(resourcePath);
                collisionTwin.Shape = externalLoaded;
                var pinned = SceneCheckpointCapture.Capture(game, "external", "session", "project", "instance", true);
                var external = pinned.Resources.Single(r => r.ResourcePath == resourcePath);
                if (external.Sha256 is null || external.Sha256.Length != 64)
                    throw new Exception("External Resource missing SHA-256 file hash");
                ((RectangleShape2D)collisionTwin.Shape).Size = new Vector2(55, 89);
                bool inMemoryRejected = false;
                try { SceneCheckpointCapture.Capture(game, "dirty", "session", "project", "instance", true); }
                catch (System.IO.InvalidDataException e)
                { inMemoryRejected = e.Message.Contains("external_resource_mutated"); }
                if (!inMemoryRejected)
                    throw new Exception("Strict capture accepted modified external Resource in memory");
                ((RectangleShape2D)collisionTwin.Shape).Size = new Vector2(21, 33);
                var bytes = System.IO.File.ReadAllBytes(diskPath);
                try
                {
                    System.IO.File.AppendAllText(diskPath, "\n# dependency changed after snapshot\n");
                    bool changedRejected = false;
                    try { var stale = SnapshotSceneBuilder.Build(pinned, true); stale.Root.Free(); }
                    catch (System.IO.InvalidDataException e)
                    { changedRejected = e.Message.Contains("resource_dependency_changed"); }
                    if (!changedRejected)
                        throw new Exception("Strict scene restore accepted modified external .tres");
                }
                finally { System.IO.File.WriteAllBytes(diskPath, bytes); }
                GD.Print("SNAPSHOT_DEPENDENCY_PASS: external Resource hash pinned and verified");
            }
            finally
            {
                collisionTwin.Shape = collision.Shape;
                externalLoaded?.Dispose();
                if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath);
            }
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
            // Godot's .tscn writer cannot always encode cyclic Resource graphs.
            // Keep the cycle in the in-memory builder probe, but export a round-trippable
            // scene in the separate end-to-end worker test.
            propsNode.CustomResource = null;
            r1.Next = null;
            r2.Next = null;
            r1.Dispose();
            r2.Dispose();
            _controller = new CaptureJobController(this, () => "probe_instance");
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
            var files = System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "recordings"), "manifest.json", System.IO.SearchOption.AllDirectories);
            if (files.Length == 0) throw new Exception("Completed capture lacks committed manifest");
            var manifest = System.Text.Json.JsonSerializer.Deserialize(
                System.IO.File.ReadAllText(files[0]), SnapshotJsonContext.Default.CheckpointManifest);
            if (manifest?.InstanceId != "probe_instance")
                throw new Exception("Checkpoint lost originating runtime instance ID");
            GD.Print("SNAPSHOT_JOB_PASS: async capture persisted and returned completed status");
            if (_testWorld is not null && GodotObject.IsInstanceValid(_testWorld))
                _testWorld.Free();
            _testWorld = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GetTree().Quit(0);
        }
        if (++_capturePollFrames > 240)
        {
            GD.PushError("SNAPSHOT_JOB_FAIL: capture timed out");
            GetTree().Quit(1);
        }
    }
}
