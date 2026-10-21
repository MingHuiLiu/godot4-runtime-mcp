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
    [Export] public Vector2I GridCell { get; set; } = new(12, 18);
    [Export] public Rect2I Region { get; set; } = new(2, 4, 12, 18);
    [Export] public Projection ProjectionMat { get; set; } = Projection.Identity;
    [Export] public Vector4[] Corners { get; set; } = [new Vector4(1, 2, 3, 4)];
}
