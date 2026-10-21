using Godot;

/// <summary>Replay fixture: only input drives the exported state.</summary>
public partial class ReplayCounter : Node2D
{
    [Export] public int Hits { get; set; }

    public override void _Input(InputEvent evt)
    {
        if (evt is InputEventKey { Keycode: Key.K, Pressed: true, Echo: false })
        {
            Hits += System.Environment.GetEnvironmentVariable(
                "GODOT_MCP_REPLAY_TEST_DIVERGE") == "1" ? 2 : 1;
        }
    }
}
