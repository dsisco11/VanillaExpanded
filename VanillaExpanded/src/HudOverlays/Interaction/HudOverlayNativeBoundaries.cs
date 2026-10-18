using System;
using System.Collections.Generic;
using System.Drawing;
using VanillaExpanded.HudOverlays.Anchoring;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Reads fixed vanilla HUD alignment surfaces without retaining or changing native dialogs and elements.</summary>
internal sealed class HudOverlayNativeBoundaries
{
    private readonly ICoreClientAPI api;
    private readonly HudOverlayHotbarAnchor hotbar;

    #region Public API
    /// <summary>Borrows client HUD discovery and the existing complete hotbar bounds adapter.</summary>
    public HudOverlayNativeBoundaries(ICoreClientAPI api)
    {
        this.api = api;
        hotbar = new HudOverlayHotbarAnchor(api);
    }

    /// <summary>Returns fixed hotbar, enabled temporal-gear hit area, health, and saturation boundaries; excludes ordinary windows.</summary>
    public IReadOnlyList<HudSnapBoundary> Resolve()
    {
        var result = new List<HudSnapBoundary>();
        if (api.HideGuis || api.Gui.LoadedGuis == null
            || api.World?.Player?.WorldData?.CurrentGameMode == EnumGameMode.Spectator) return result;
        if (hotbar.Resolve() is { } toolbar)
        {
            result.Add(new(HudOverlayTargets.Hotbar, toolbar));
            // The native composer creates this hover element only when the temporal gear is enabled. Its public
            // hit area is a stable reference; do not duplicate the gear renderer's private offsets or jitter.
            foreach (var dialog in api.Gui.LoadedGuis)
                if (dialog is HudHotbar native && native.Composers["hotbar"]?.GetElement("tempStabHoverText") is { } hover
                    && ReadBounds(hover.Bounds) is { } gear) result.Add(new(HudOverlayTargets.TemporalStability, gear));
        }
        HudStatbar? statbar = null;
        // Ambiguous replacements provide no authority. Re-read every drag update so resizing/rebinding cannot leave stale guides.
        foreach (var dialog in api.Gui.LoadedGuis)
        {
            if (dialog is not HudStatbar candidate) continue;
            if (statbar != null) return result;
            statbar = candidate;
        }
        if (statbar == null || !statbar.IsOpened()) return result;
        var composer = statbar.Composers["statbar"];
        if (composer == null || !composer.Enabled || ReadBounds(composer.Bounds) == null) return result;
        foreach (string key in new[] { HudOverlayTargets.HealthElement, HudOverlayTargets.SaturationElement })
            if (composer.GetElement(key) is GuiElementStatbar meter && ReadBounds(meter.Bounds) is { } rectangle)
                result.Add(new(key, rectangle));
        return result;
    }
    #endregion

    #region Private
    /// <summary>Reads initialized physical extents including parent offsets, rejecting pending or nonfinite native geometry.</summary>
    private static RectangleF? ReadBounds(ElementBounds? bounds)
    {
        if (bounds == null || !bounds.Initialized || bounds.RequiresRecalculation) return null;
        double x = bounds.renderX, y = bounds.renderY, width = bounds.OuterWidth, height = bounds.OuterHeight;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue || width <= 0 || height <= 0
            || width > float.MaxValue || height > float.MaxValue) return null;
        return new RectangleF((float)x, (float)y, (float)width, (float)height);
    }
    #endregion
}
