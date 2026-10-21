using Godot;
using GodotRuntimeMcp.Recording;

public partial class ReplayCaptureProbe : Node
{
    private RuntimeTimelineController? _controller;
    private ReplayCounter? _counter;
    private string _sessionId = "";
    private bool _sent;
    private int _ticks;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _counter = new ReplayCounter { Name = "ReplayCounter" };
        AddChild(_counter);
        _controller = new RuntimeTimelineController(this, () => "replay_fixture");
        var status = _controller.Start(_counter.GetPath().ToString(), 10, 3,
            strict: true, captureInput: true);
        if (status.Status is not ("starting" or "running"))
            throw new System.Exception("Replay test could not start recording");
        _sessionId = status.SessionId;
    }
    public override void _Input(InputEvent input) => _controller?.ObserveInput(input);
    public override void _Process(double delta)
    {
        try
        {
            var status = _controller!.Status(_sessionId);
            if (status.SampleCount >= 1 && !_sent)
            {
                using var key = new InputEventKey { Keycode = Key.K,
                    PhysicalKeycode = Key.K, Pressed = true };
                Input.ParseInputEvent(key);
                _sent = true;
            }
            _controller.Pump();
            if (status.Status == "completed")
            {
                if (!_sent || _counter!.Hits != 1 || status.SampleCount != 3 ||
                    status.DroppedSamples != 0 || status.DroppedObservedEvents != 0)
                    throw new System.Exception("Unexpected source: hits=" + _counter?.Hits +
                        " samples=" + status.SampleCount + " dropped=" + status.DroppedSamples);
                string root = System.Environment.GetEnvironmentVariable("GODOT_MCP_CONFIG_DIR")!;
                string timeline = System.IO.Path.Combine(root, "recordings", status.ProjectId,
                    status.SessionId, "timeline");
                var batches = TimelineArchive.ReadPage(timeline, 0, 10);
                var plan = ReplayPlanBuilder.Build(status, batches.Items);
                if (plan.Steps[1].Events.Count != 1)
                    throw new System.Exception("Missing input event in replay source");
                GD.Print("REPLAY_CAPTURE_PASS: session=" + _sessionId +
                    " steps=" + plan.Steps.Count + " hits=" + _counter.Hits);
                _counter.Free();
                _counter = null;
                GetTree().Quit(0);
            }
            if (status.Status == "failed" || ++_ticks > 500)
                throw new System.Exception("Replay source failed: " + status.Code +
                    " " + status.Message + " sample=" + status.SampleCount);
        }
        catch (System.Exception ex)
        {
            GD.PushError("REPLAY_CAPTURE_FAIL: " + ex);
            GetTree().Quit(1);
        }
    }
}
