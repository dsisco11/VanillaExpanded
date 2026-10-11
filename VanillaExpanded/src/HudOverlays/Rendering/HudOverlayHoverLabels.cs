using System;
using System.Collections.Generic;
using System.Drawing;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Resolves visible pointer targets and owns one native tooltip for the session.</summary>
internal sealed class HudOverlayHoverLabels : IDisposable
{
    private readonly ICoreClientAPI api;
    private GuiElementHoverText? tooltip;
    private string? text;
    private double scale;
    #region Public API
    /// <summary>Borrows client pointer and localization services without capturing input.</summary>
    public HudOverlayHoverLabels(ICoreClientAPI api) => this.api = api;

    /// <summary>Finds a labeled, drawable member only when the player has a free pointer.</summary>
    internal static HudOverlayRegistration? FindTarget(IEnumerable<HudOverlayGroupLayout> groups,
        Func<HudOverlayRegistration, bool> isDrawable, bool mouseGrabbed, PointF pointer)
    {
        if (mouseGrabbed) return null;
        foreach (var group in groups)
        {
            if (!group.Available) continue;
            foreach (var member in group.Members)
                if (!string.IsNullOrWhiteSpace(member.Registration.HoverLabelKey)
                    && member.Clip.Width > 0 && member.Clip.Height > 0
                    && member.Clip.Contains(pointer) && isDrawable(member.Registration))
                    return member.Registration;
        }
        return null;
    }

    /// <summary>Renders native hover text outside indicator scissors, rebuilding only for text or scale changes.</summary>
    public void Draw(IEnumerable<HudOverlayGroupLayout> groups, Func<HudOverlayRegistration, bool> isDrawable, float dt)
    {
        if (api.Input == null) return;
        var target = FindTarget(groups, isDrawable, api.Input.MouseGrabbed, new PointF(api.Input.MouseX, api.Input.MouseY));
        if (target == null) return;
        string localized = Lang.Get(target.HoverLabelKey!);
        // Resolve every hover in the current locale, but retain the GPU resources while the resolved label is unchanged.
        if (tooltip == null || text != localized || scale != RuntimeEnv.GUIScale)
        {
            Dispose();
            text = localized;
            scale = RuntimeEnv.GUIScale;
            tooltip = new GuiElementHoverText(api, localized, CairoFont.WhiteSmallText(), 280,
                ElementBounds.Fixed(0, 0, 1, 1).WithEmptyParent());
            tooltip.SetAutoDisplay(false);
            tooltip.SetAutoWidth(true);
            tooltip.SetVisible(true);
        }
        tooltip.RenderInteractiveElements(dt);
    }

    /// <summary>Releases the native tooltip texture and rich text when the host ends its session.</summary>
    public void Dispose()
    {
        tooltip?.Dispose();
        tooltip = null;
        text = null;
    }
    #endregion
}
