using System;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Reads the visible native saturation meter through public engine surfaces.</summary>
internal sealed class HudOverlaySaturationAnchor
{
    private readonly ICoreClientAPI api;

    #region Public API
    /// <summary>Stores borrowed client services without retaining native dialogs or composers.</summary>
    public HudOverlaySaturationAnchor(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }

    /// <summary>Returns no anchor for hidden, missing, ambiguous, closed or uninitialized saturation meters.</summary>
    public RectangleF? Resolve()
    {
        if (api.HideGuis || api.World?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Spectator) return null;
        HudStatbar? statbar = null;
        // Count native candidates before testing visibility: multiple replacements must
        // never make registration order choose an arbitrary placement authority.
        foreach (var dialog in api.Gui.LoadedGuis)
        {
            if (dialog is not HudStatbar candidate) continue;
            if (statbar != null) return null;
            statbar = candidate;
        }
        if (statbar == null || !statbar.IsOpened()) return null;
        var composer = statbar.Composers["statbar"];
        if (composer == null || !composer.Enabled) return null;
        // The installed statbar composer exposes the actual meter element, avoiding toolbar offsets.
        if (composer.GetElement("saturationstatbar") is not GuiElementStatbar meter) return null;
        var bounds = meter.Bounds;
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
