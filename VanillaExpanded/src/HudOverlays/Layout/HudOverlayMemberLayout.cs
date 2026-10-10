using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Layout;

/// <summary>One retained presentation's native child bounds and framebuffer-pixel drawing clip.</summary>
internal sealed class HudOverlayMemberLayout
{
    public HudOverlayRegistration Registration { get; }
    public ElementBounds Bounds { get; }
    public RectangleF Clip { get; internal set; }
    #region Public API
    /// <summary>Associates an existing native child with its registered presentation; owns no feature resources.</summary>
    public HudOverlayMemberLayout(HudOverlayRegistration registration, ElementBounds bounds)
    {
        Registration = registration;
        Bounds = bounds;
    }
    #endregion
}
