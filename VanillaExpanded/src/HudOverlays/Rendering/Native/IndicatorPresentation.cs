using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Retains prepared indicator data and visibility while composer elements own GUI resources.</summary>
internal abstract class IndicatorPresentation : IDisposable
{
    private readonly Func<HudOverlayPreparationContext, string, SizeF>? measure;
    private readonly Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText;

    #region Public API
    /// <summary>Gets the shared feature transition.</summary>
    public HudOverlayVisibilityTransition Visibility { get; }
    /// <summary>Gets canonical prepared dimensions in GUI units.</summary>
    public SizeF Size { get; protected set; }
    /// <summary>Gets the selected supported style.</summary>
    public HudIndicatorStyle Style { get; private set; }
    /// <summary>Changes presentation structure without clearing sampled data or animation timing.</summary>
    public bool SetStyle(HudIndicatorStyle value)
    {
        var next = value == HudIndicatorStyle.Text ? value : HudIndicatorStyle.Icon;
        if (next == Style) return false;
        Style = next; Size = SizeF.Empty;
        return true;
    }
    /// <summary>Retains prepared data until the common exit finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Clears prepared and sampled state after an exit or world change.</summary>
    public void Reset()
    {
        Visibility.Reset(); Size = SizeF.Empty; Text = null; ClearContent();
    }
    /// <summary>Releases controller state; generated resources belong to the host's composers.</summary>
    public void Dispose() => Reset();
    #endregion

    #region Protected API
    /// <summary>Accepts CPU metrics and optional native glyph creation for focused headless checks.</summary>
    protected IndicatorPresentation(bool flash = false,
        Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null)
    {
        this.measure = measure; this.createText = createText;
        Visibility = new(flash: flash);
    }
    /// <summary>Gets prepared localized text without retaining element resources.</summary>
    protected TextContent? Text { get; private set; }
    /// <summary>Measures a reading without generating a duplicate glyph texture.</summary>
    protected SizeF MeasureText(HudOverlayPreparationContext context, string value, double strokeWidth = 1) =>
        measure != null ? measure(context, value) : TextMetrics.Measure(context, value, strokeWidth);
    /// <summary>Publishes localized two-line content and its canonical footprint.</summary>
    protected void PrepareText(HudOverlayPreparationContext context, string shortName, string reading)
    {
        string name = Lang.GetL(context.Locale, "vanillaexpanded:hud-short-" + shortName);
        Text = new(name, reading, MeasureText(context, name), MeasureText(context, reading));
        Size = Text.Size; Visibility.Show();
    }
    /// <summary>Creates an independent text composition borrowing only prepared strings and metrics.</summary>
    protected INativeView CreateTextView(ICoreClientAPI api, Func<float>? effect = null) =>
        new TextView(api, () => Text ?? throw new InvalidOperationException("Prepare indicator text before its view."), effect, createText);
    /// <summary>Gets the optional per-element glyph factory.</summary>
    protected Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? CreateText => createText;
    /// <summary>Clears feature-specific state without disposing composer resources.</summary>
    protected abstract void ClearContent();
    #endregion
}
