using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns a cached short indicator name and its optional sampled reading, without loading artwork.</summary>
internal sealed class HudIndicatorTextPresentation : IDisposable
{
    private readonly WorkshopGaugeText name;
    private readonly WorkshopGaugeText reading;
    private readonly Vec4f tint = new(1, 1, 1, 1);
    private HudOverlayPreparationContext? context;
    private bool hasReading;

    #region Public API
    #region Preparation and drawing
    /// <summary>Accepts owned native text helpers or headless substitutes.</summary>
    public HudIndicatorTextPresentation(WorkshopGaugeText? name = null, WorkshopGaugeText? reading = null)
    {
        this.name = name ?? new();
        this.reading = reading ?? new();
    }

    /// <summary>Gets the content-sized text footprint, with the standard indicator height.</summary>
    public SizeF Size { get; private set; }

    /// <summary>Prepares a localized short name and preserves all supplied reading characters at the normal HUD font size.</summary>
    public void Prepare(HudOverlayPreparationContext preparation, string shortName, string? value = null)
    {
        name.SetContent(Lang.GetL(preparation.Locale, "vanillaexpanded:hud-short-" + shortName));
        name.Prepare(preparation);
        hasReading = !string.IsNullOrEmpty(value);
        if (hasReading) { reading.SetContent(value!); reading.Prepare(preparation); }
        else reading.Reset();
        context = preparation;
        Size = new(Math.Max(HudOverlaySizes.Wide, Math.Max(name.Size.Width, reading.Size.Width)), HudOverlaySizes.Standard);
    }

    /// <summary>Draws cached text with the host fade and an optional urgency pulse.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, float opacity = 1)
    {
        if (context == null) return;
        float scale = (float)context.GuiScale;
        float height = HudOverlaySizes.Standard * scale;
        float y = (float)(bounds.renderY + (bounds.OuterHeight - height) / 2);
        float alpha = DrawOpacity.Current * opacity;
        tint.Set(alpha, alpha, alpha, alpha);
        var area = new RectangleF((float)bounds.renderX, y, Size.Width * scale, hasReading ? height / 2 : height);
        name.Draw(renderer, area, 50, tint);
        if (hasReading) reading.Draw(renderer, area with { Y = y + height / 2 }, 50, tint);
    }
    #endregion

    #region Lifetime
    /// <summary>Releases session-owned text after exit transitions, style changes, or world changes.</summary>
    public void Reset()
    {
        name.Reset(); reading.Reset(); context = null; hasReading = false; Size = SizeF.Empty;
    }

    /// <summary>Ends ownership of both cached text helpers.</summary>
    public void Dispose()
    {
        Reset(); name.Dispose(); reading.Dispose();
    }
    #endregion
    #endregion
}
