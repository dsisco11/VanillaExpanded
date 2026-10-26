using System.Drawing;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Describes one stable icon/optional-reading arrangement in caller-supplied GUI units.</summary>
internal sealed record IconReadingContent(string? IconPath, SizeF IconSize, string? Reading,
    float Gap, float MinimumHeight = 0, float MaximumReadingHeight = float.PositiveInfinity);
