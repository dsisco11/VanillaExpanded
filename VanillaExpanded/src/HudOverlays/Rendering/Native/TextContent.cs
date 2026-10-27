using System.Drawing;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Retains localized text and canonical metrics without owning glyph textures.</summary>
internal sealed record TextContent(string Name, string Reading, SizeF NameSize, SizeF ReadingSize)
{
    /// <summary>Gets the standard-height footprint expanded to preserve both text lines.</summary>
    public SizeF Size => new(System.Math.Max(HudOverlaySizes.Wide, System.Math.Max(NameSize.Width, ReadingSize.Width)), HudOverlaySizes.Standard);
}
