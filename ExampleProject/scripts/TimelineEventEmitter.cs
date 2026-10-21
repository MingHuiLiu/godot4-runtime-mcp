using Godot;

/// <summary>Test-only Godot signal with both safe numeric and confidential text arguments.</summary>
public partial class TimelineEventEmitter : Node2D
{
    [Signal] public delegate void ImpactEventHandler(int damage, string privateText);
}
