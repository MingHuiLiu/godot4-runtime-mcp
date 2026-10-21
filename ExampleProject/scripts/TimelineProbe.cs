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
    private bool _inputSent;
    private bool _renamed;
    private bool _removed;
    private bool _stoppingTrial;
    private bool _stopRequested;
    private bool _flushTrial;
    private bool _flushRequested;
    private int _frames;

    public TimelineProbe() => _controller = new RuntimeTimelineController(this, () => "timeline_probe");

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _world = new Node2D { Name = "TimelineWorld" };
        AddChild(_world);
        var started = _controller.Start(_world.GetPath().ToString(), 1, 4, true,
            captureSignals: true, captureInput: true);
        if (started.Status is not ("starting" or "running"))
        {
            GD.PushError("TIMELINE_PROBE_FAIL: start " + started.Code + ":" + started.Message);
            GetTree().Quit(1);
        }
        _sessionId = started.SessionId;
    }

    public override void _Input(InputEvent evt) => _controller.ObserveInput(evt);

    public override void _Process(double delta)
    {
        try
        {
            // Mutate before sampling this frame. Otherwise a worker completing
            // asynchronously may let Pump observe the old tree first.
            var current = _controller.Status(_sessionId);
            if (!_stoppingTrial && current.SampleCount >= 1 && !_added)
            {
                _child = new TimelineEventEmitter { Name = "Born" };
                _world!.AddChild(_child);
                // Signal is genuinely emitted by Godot after the node is renamed.
                _child.Renamed += () => _controller.ObserveSignal(_child, "renamed");
                ((TimelineEventEmitter)_child).Impact += (int damage, string privateText) =>
                    _controller.ObserveSignal(_child!, "impact", Variant.From(damage),
                        Variant.From(privateText));
                // Exercise real Godot Input routing; omit Unicode/text from archival data.
                using var key = new InputEventKey { Keycode = Key.A,
                    PhysicalKeycode = Key.A, Pressed = true, Unicode = 0x2603 };
                Input.ParseInputEvent(key);
                // A signal emitted outside the selected scene subtree must be filtered.
                var outside = new Node2D { Name = "Outside" };
                AddChild(outside);
                outside.Renamed += () => _controller.ObserveSignal(outside, "outside_scope");
                outside.Name = "OutsideChanged";
                outside.Free();
                // A full buffer must record an explicit loss count, not allocate without bound.
                for (int attempt = 0; attempt < 300; attempt++)
                    _controller.ObserveInput(key);
                _inputSent = true;
                _added = true;
            }
            if (!_stoppingTrial && current.SampleCount >= 2 && !_renamed)
            {
                _child!.Name = "Changed";
                _child.EmitSignal(TimelineEventEmitter.SignalName.Impact,
                    9, "SHOULD_NEVER_BE_PERSISTED");
                _renamed = true;
            }
            if (!_stoppingTrial && current.SampleCount >= 3 && !_removed)
            {
                _child!.Free();
                _removed = true;
            }
            if (_stoppingTrial && current.SampleCount >= 1 && !_stopRequested)
            {
                // The second recording omits capture flags: both callbacks are ignored.
                using var key = new InputEventKey { Keycode = Key.B, Pressed = true };
                _controller.ObserveInput(key);
                _controller.ObserveSignal(_world!, "renamed");
            }
            if (_stoppingTrial && current.SampleCount >= 1 && !_stopRequested)
            {
                var stop = _controller.Stop(_sessionId);
                if (stop.Status != "stopping")
                    throw new Exception("Stop did not enter stopping state");
                _stopRequested = true;
            }
            if (_flushTrial && current.SampleCount >= 1 && !_flushRequested)
            {
                using var key = new InputEventKey { Keycode = Key.C, Pressed = true,
                    Unicode = 0x2603 };
                _controller.ObserveInput(key);
                var stop = _controller.Stop(_sessionId);
                if (stop.Status != "stopping")
                    throw new Exception("Flush-on-stop request rejected");
                _flushRequested = true;
            }
            _controller.Pump();
            if (!_stoppingTrial && current.Status == "completed")
            {
                string home = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
                string timeline = Path.Combine(home, "recordings", current.ProjectId,
                    current.SessionId, "timeline");
                var events = TimelineArchive.ReadPage(timeline, 0, 10);
                var kinds = events.Items.SelectMany(x => x.Events).Select(x => x.Kind).ToHashSet();
                var observed = events.Items.SelectMany(x => x.ObservedEvents ?? new()).ToList();
                int dropped = events.Items.Sum(x => x.DroppedObservedEvents);
                if (dropped <= 0 || dropped != current.DroppedObservedEvents)
                    throw new Exception("Event backpressure was not preserved in status/batches: " + dropped);
                GD.Print("TIMELINE_OVERFLOW_PASS: bounded pending events with visible drop count=" + dropped);
                var impact = observed.FirstOrDefault(e => e.Kind == "signal" && e.Name == "impact");
                if (observed.Any(e => e.Name == "outside_scope"))
                    throw new Exception("Outside-subtree signal leaked into selected recording");
                GD.Print("TIMELINE_SCOPE_PASS: unrelated scene signals excluded");
                if (impact is null || impact.Fields["arg0"].Data?.GetValue<long>() != 9 ||
                    impact.Fields["arg1"].Type != "redacted" ||
                    observed.Any(e => e.Fields.Values.Any(v =>
                        v.Data?.ToJsonString().Contains("SHOULD_NEVER_BE_PERSISTED") == true)))
                    throw new Exception("Sensitive signal parameters were not redacted");
                GD.Print("TIMELINE_REDACTION_PASS: real parameter signal delivers numeric metadata without private text");
                if (!_inputSent || !observed.Any(e => e.Kind == "input" && e.Name == "key") ||
                    !observed.Any(e => e.Kind == "signal" && e.Name == "renamed") ||
                    observed.SelectMany(e => e.Fields.Keys).Any(k =>
                        k.Contains("unicode", StringComparison.OrdinalIgnoreCase) ||
                        k.Contains("text", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Missing real signal/input callback, or text was disclosed: " +
                        string.Join(",", observed.Select(x => x.Kind + ":" + x.Name)));
                GD.Print("TIMELINE_EVENT_PASS: genuine Godot key and renamed callbacks persisted without Unicode");
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
                var stoppedPage = TimelineArchive.ReadPage(timeline, 0, 10);
                if (stoppedPage.Items.Any(b => (b.ObservedEvents?.Count ?? 0) != 0))
                    throw new Exception("Opt-out session unexpectedly archived signal or input events");
                GD.Print("TIMELINE_OPTOUT_PASS: default-disabled input and signal callbacks ignored");
                if (!_stopRequested || persisted.Status != "stopped" || persisted.SampleCount < 1)
                    throw new Exception("Stopped timeline status was not persisted");
                GD.Print("TIMELINE_STOP_PASS: stop request durably closes sample recording");
                var flush = _controller.Start(_world!.GetPath().ToString(), 1, 10,
                    strict: true, captureInput: true);
                if (flush.Status is not ("starting" or "running"))
                    throw new Exception("Could not start follow-up stop-flush timeline");
                _sessionId = flush.SessionId;
                _stoppingTrial = false;
                _flushTrial = true;
                return;
            }
            if (_flushTrial && current.Status == "stopped")
            {
                string home = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
                string timeline = Path.Combine(home, "recordings", current.ProjectId,
                    current.SessionId, "timeline");
                var page = TimelineArchive.ReadPage(timeline, 0, 10);
                if (!_flushRequested || current.SampleCount < 2 ||
                    !page.Items.SelectMany(b => b.ObservedEvents ?? new()).Any(e =>
                        e.Kind == "input" && e.Name == "key"))
                    throw new Exception("Events queued immediately before stop were not committed");
                GD.Print("TIMELINE_STOP_FLUSH_PASS: final pending input persisted before stop");
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
