using System.Drawing;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Captures the host-owned conditions under which a prepared popup remains valid.</summary>
internal sealed record HudMenuEnvironment(object Session, RectangleF Viewport, double GuiScale, string Locale,
    bool EditingAvailable);
