using System;

namespace VanillaExpanded.RadialMenu;

/// <summary>Describes one semantic menu option without exposing its caller's selection policy.</summary>
public sealed record RadialMenuEntry
{
    /// <summary>Creates a generic option for a fixed layout identifier.</summary>
    public RadialMenuEntry(string id, string label, bool enabled, IRadialMenuIcon? icon = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An entry needs a stable identifier.", nameof(id));
        Id = id;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Enabled = enabled;
        Icon = icon;
        Description = description;
    }

    /// <summary>Gets the stable identifier used by the owning caller.</summary>
    public string Id { get; }
    /// <summary>Gets the displayed label.</summary>
    public string Label { get; }
    /// <summary>Gets whether the option can be selected.</summary>
    public bool Enabled { get; }
    /// <summary>Gets an optional game-rendered icon.</summary>
    public IRadialMenuIcon? Icon { get; }
    /// <summary>Gets optional explanatory text shown separately from the label.</summary>
    public string? Description { get; }
}

