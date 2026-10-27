using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Retains instrument readings and canonical measurements while elements own visual resources.</summary>
internal abstract class InstrumentPresentation : IDisposable
{
    private readonly Func<HudOverlayPreparationContext, string, SizeF>? measure;
    private TextContent? text;

    #region Public API
    /// <summary>Gets the selected instrument style.</summary>
    public EnvironmentalGaugeStyle Style { get; private set; }
    /// <summary>Gets the measured footprint in GUI units.</summary>
    public SizeF Size { get; protected set; }
    /// <summary>Gets the feature-owned visibility envelope.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Changes visual structure without clearing sampled data or estimation timing.</summary>
    public bool SetStyle(EnvironmentalGaugeStyle value)
    {
        if (!Enum.IsDefined(value)) value = EnvironmentalGaugeStyle.Immersive;
        if (Style == value) return false;
        Style = value; Size = SizeF.Empty;
        return true;
    }
    /// <summary>Retains prepared content until the exit envelope finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Clears feature data after world teardown or a completed exit.</summary>
    public void Reset()
    {
        Visibility.Reset(); Size = SizeF.Empty; text = null; Compact = null; ClearContent();
    }
    /// <summary>Releases controller data; composers retain exclusive ownership of mounted resources.</summary>
    public void Dispose() => Reset();
    #endregion

    #region Protected API
    /// <summary>Accepts CPU metrics and independent per-element resource factories.</summary>
    protected InstrumentPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null)
    {
        this.measure = measure; CreateText = createText; Load = load;
    }
    /// <summary>Gets the optional native glyph factory.</summary>
    protected Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? CreateText { get; }
    /// <summary>Gets the optional artwork binding factory.</summary>
    protected Func<HudOverlayPreparationContext, string, TextureBinding>? Load { get; }
    /// <summary>Gets the prepared compact arrangement.</summary>
    protected IconReadingContent? Compact { get; private set; }
    /// <summary>Prepares the shared text or compact style without allocating GUI resources.</summary>
    protected bool PrepareSimple(HudOverlayPreparationContext context, string name, string reading, string icon, bool iconReading = true)
    {
        if (Style == EnvironmentalGaugeStyle.Immersive) return false;
        if (Style == EnvironmentalGaugeStyle.Text)
        {
            string label = Lang.GetL(context.Locale, "vanillaexpanded:hud-short-" + name);
            text = new(label, reading, Measure(context, label), Measure(context, reading));
            Size = text.Size;
        }
        else
        {
            // Compact time has no reading; temperature and date keep their measured row above smaller artwork.
            string? value = iconReading ? reading : null;
            var metrics = value == null ? SizeF.Empty : Measure(context, value);
            float diameter = value == null ? HudOverlaySizes.Standard : HudOverlaySizes.Small;
            Compact = new(icon, new(diameter, diameter), value, HudOverlaySizes.ReadingGap, ReadingSize: metrics);
            Size = new(Math.Max(metrics.Width, diameter), metrics.Height + (metrics.Height > 0 ? HudOverlaySizes.ReadingGap : 0) + diameter);
        }
        Visibility.Show(); return true;
    }
    /// <summary>Creates a separate native text or icon composition over stable feature data.</summary>
    protected INativeView CreateSimpleView(ICoreClientAPI api) => Style == EnvironmentalGaugeStyle.Text
        ? new TextView(api, () => text ?? throw new InvalidOperationException("Prepare text before its view."), create: CreateText)
        : new IconReadingView(api, () => Compact ?? throw new InvalidOperationException("Prepare compact content before its view."), _ => { }, CreateText, Load);
    /// <summary>Clears only feature data; GUI resources belong to elements.</summary>
    protected abstract void ClearContent();
    #endregion

    #region Private
    /// <summary>Measures localized glyph boxes without allocating duplicate textures.</summary>
    private SizeF Measure(HudOverlayPreparationContext context, string value) => measure != null ? measure(context, value) : TextMetrics.Measure(context, value);
    #endregion
}
