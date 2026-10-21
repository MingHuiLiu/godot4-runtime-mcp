using Godot;
using GodotRuntimeMcp.Recording;
using System;
using System.IO;
using System.Linq;

public partial class TimelineProbe : Node
{
    private readonly RuntimeTimelineController _controller;
    private Node2D? _world;
    private Node2D? _child;
    private string _sessionId = "";
    private bool _added;
    private bool _renamed;
    private bool _removed;
    private bool _stoppingTrial;
    private bool _stopRequested;
    private int _frames;

    public TimelineProbe() => _controller = new RuntimeTimelineController(this, () => "timeline_probe");

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _world = new Node2D { Name = "TimelineWorld" };
        AddChild(_world);
        var started = _controller.Start(_world.GetPath().ToString(), 1, 4, true);
        if (started.Status is not ("starting" or "running"))
        {
            GD.PushError("TIMELINE_PROBE_FAIL: start " + started.Code + ":" + started.Message);
            GetTree().Quit(1);
        }
        _sessionId = started.SessionId;
    }

    public override void _Process(double delta)
    {
        try
        {
            // Mutate before sampling this frame. Otherwise a worker completing
            // asynchronously may let Pump observe the old tree first.
            var current = _controller.Status(_sessionId);
            if (!_stoppingTrial && current.SampleCount >= 1 && !_added)
            {
                _child = new Node2D { Name = "Born" };
                _world!.AddChild(_child);
                _added = true;
            }
            if (!_stoppingTrial && current.SampleCount >= 2 && !_renamed)
            {
                _child!.Name = "Changed";
                _renamed = true;
            }
            if (!_stoppingTrial && current.SampleCount >= 3 && !_removed)
            {
                _child!.Free();
                _removed = true;
            }
            if (_stoppingTrial && current.SampleCount >= 1 && !_stopRequested)
            {
                var stop = _controller.Stop(_sessionId);
                if (stop.Status != "stopping")
                    throw new Exception("Stop did not enter stopping state");
                _stopRequested = true;
            }
            _controller.Pump();
            if (!_stoppingTrial && current.Status == "completed")
            {
                string home = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
                string timeline = Path.Combine(home, "recordings", current.ProjectId,
                    current.SessionId, "timeline");
                var events = TimelineArchive.ReadPage(timeline, 0, 10);
                var kinds = events.Items.SelectMany(x => x.Events).Select(x => x.Kind).ToHashSet();
                if (current.SampleCount != 4 || events.Total != 4 ||
                    !kinds.Contains("node_added") || !kinds.Contains("node_renamed") ||
                    !kinds.Contains("node_removed") || current.InstanceId != "timeline_probe")
                    throw new Exception("Incomplete timeline: " + string.Join(",", kinds));
                GD.Print("TIMELINE_PROBE_PASS: 4 persisted samples, add/rename/remove, source instance");
                var next = _controller.Start(_world!.GetPath().ToString(), 1, 20, true);
                if (next.Status is not ("starting" or "running"))
                    throw new Exception("Could not start follow-up timeline after completion");
                _sessionId = next.SessionId;
                _stoppingTrial = true;
                return;
            }
            if (_stoppingTrial && current.Status == "stopped")
            {
                string home = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
                string timeline = Path.Combine(home, "recordings", current.ProjectId,
                    current.SessionId, "timeline");
                var persisted = TimelineArchive.ReadStatus(timeline);
                if (!_stopRequested || persisted.Status != "stopped" || persisted.SampleCount < 1)
                    throw new Exception("Stopped timeline status was not persisted");
                GD.Print("TIMELINE_STOP_PASS: stop request durably closes sample recording");
                _world!.Free();
                _world = null;
                GetTree().Quit(0);
            }
            if (current.Status == "failed")
                throw new Exception(current.Code + ": " + current.Message);
            if (++_frames > 1200)
                throw new Exception("Timeline timed out at " + current.Status +
                    " after " + current.SampleCount + " samples");
        }
        catch (Exception ex)
        {
            GD.PushError("TIMELINE_PROBE_FAIL: " + ex);
            GetTree().Quit(1);
        }
    }
}
