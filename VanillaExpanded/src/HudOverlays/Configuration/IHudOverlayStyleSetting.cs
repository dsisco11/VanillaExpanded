using System;
using System.Collections.Generic;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Exposes enum-backed style choices across heterogeneous feature registrations.</summary>
public interface IHudOverlayStyleSetting
{
    /// <summary>Gets the current authoritative enum value.</summary>
    Enum Value { get; }
    /// <summary>Gets the supported enum values without string-based configuration conversion.</summary>
    IReadOnlyList<Enum> Choices { get; }
    /// <summary>Changes a supported value through the owning configuration binding.</summary>
    void Set(Enum value);
}

