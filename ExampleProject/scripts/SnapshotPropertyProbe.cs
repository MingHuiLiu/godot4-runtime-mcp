using Godot;

public partial class SnapshotPropertyProbe : Node2D
{
    [Export] public Godot.Collections.Array<string> Tags { get; set; } =
        new() { "alpha", "beta" };
    [Export] public Godot.Collections.Dictionary<string, int> Counts { get; set; } =
        new() { ["alpha"] = 1, ["beta"] = 2 };
    [Export] public Resource? CustomResource { get; set; }
    [Export] public Vector2[] Vertices { get; set; } = [new Vector2(1, 2), new Vector2(3, 4)];
    [Export] public string BigText { get; set; } = "";
}
