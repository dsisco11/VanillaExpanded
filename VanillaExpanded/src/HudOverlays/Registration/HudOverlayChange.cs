using System;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Separates sampled presentation changes from measured-size changes.</summary>
[Flags]
internal enum HudOverlayChange
{
    None = 0,
    Presentation = 1,
    Measurement = 2
}
