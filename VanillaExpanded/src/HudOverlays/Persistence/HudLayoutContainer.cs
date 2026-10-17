using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Persistence;

/// <summary>Stores a stable container identity, layout description, explicit permission, and optional screen origin.</summary>
internal sealed record HudLayoutContainer
{
    public string Id { get; init; } = "";
    public string Key { get; init; } = "";
    public string? TemplateId { get; init; }
    public bool Movable { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public double Padding { get; init; }
    public HudLayoutNode Tree { get; init; } = new();
}
