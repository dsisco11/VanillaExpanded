using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace VanillaExpanded.HudOverlays.Layout;

/// <summary>Reserves the original source footprint during member dragging without adding a duplicate layout member.</summary>
internal sealed record HudOverlaySourceReservation(PointF Origin, SizeF Size, IReadOnlyDictionary<string, RectangleF> Rectangles)
{
    #region Public API
    /// <summary>Captures GUI-unit geometry independently of mutable native bounds.</summary>
    public static HudOverlaySourceReservation Capture(HudOverlayGroupLayout group, PointF windowOrigin, double scale) => new(
        new PointF((float)((group.Root.renderX - windowOrigin.X) / scale), (float)((group.Root.renderY - windowOrigin.Y) / scale)),
        new SizeF((float)(group.Root.OuterWidth / scale), (float)(group.Root.OuterHeight / scale)),
        group.Members.ToDictionary(member => member.Registration.Id, member => new RectangleF(
            (float)((member.Bounds.renderX - group.Root.renderX) / scale), (float)((member.Bounds.renderY - group.Root.renderY) / scale),
            (float)(member.Bounds.OuterWidth / scale), (float)(member.Bounds.OuterHeight / scale))));
    #endregion
}
