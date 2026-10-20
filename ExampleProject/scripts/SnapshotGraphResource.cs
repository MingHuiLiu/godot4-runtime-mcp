using Godot;

public partial class SnapshotGraphResource : Resource
{
    [Export] public Resource? Next { get; set; }
}
