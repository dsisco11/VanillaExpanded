using System;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Reads the complete visible native hotbar composer through public engine surfaces.</summary>
internal sealed class HudOverlayHotbarAnchor
{
    private readonly ICoreClientAPI api;

    #region Public API
    /// <summary>Stores borrowed client services without retaining native dialogs or composers.</summary>
    public HudOverlayHotbarAnchor(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Returns no anchor for hidden, missing, ambiguous, closed or uninitialized hotbars.</summary>
    public RectangleF? Resolve()
    {
        if (api.HideGuis) return null;
        HudHotbar? hotbar = null;
        // Count native candidates before testing visibility: multiple replacements must
        // never make registration order choose an arbitrary placement authority.
        foreach (var dialog in api.Gui.LoadedGuis)
        {
            if (dialog is not HudHotbar candidate) continue;
            if (hotbar != null) return null;
            hotbar = candidate;
        }
        if (hotbar == null || !hotbar.IsOpened()) return null;
        var composer = hotbar.Composers["hotbar"];
        if (composer == null || !composer.Enabled) return null;
        var bounds = composer.Bounds;
        if (bounds == null || !bounds.Initialized || bounds.RequiresRecalculation) return null;
        double x = bounds.renderX, y = bounds.renderY;
        double width = bounds.OuterWidth, height = bounds.OuterHeight;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0 || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue
            || width > float.MaxValue || height > float.MaxValue) return null;
        return new RectangleF((float)x, (float)y, (float)width, (float)height);
    }
    #endregion
}
