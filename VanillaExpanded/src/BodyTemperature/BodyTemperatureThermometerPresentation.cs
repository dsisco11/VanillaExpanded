using System;
using System.Drawing;
using Vintagestory.API.Common;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Borrows asset-backed thermometer textures and owns whole-degree text; drawing crops and tints a cached full silhouette mask.</summary>
internal sealed class BodyTemperatureThermometerPresentation : IDisposable
{
    internal const float IconWidth = 14, IconHeight = 24, Gap = 3;
    private readonly HudOverlayIconTextPresentation text;
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> createIcons;
    private LoadedTexture[]? icons;
    private HudOverlayPreparationContext? context;
    private BodyTemperatureSample? sample;
    private bool showReading;
    private readonly ElementBounds fillClip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private readonly Vec4f tint = new(1, 1, 1, 1);
    private readonly Vec4f warmingTint = new(1, .65f, .2f, 1);
    private readonly Vec4f coolingTint = new(.25f, .6f, 1, 1);
    public SizeF Size { get; private set; }

    #region Public API
    #region Content and presentation
    /// <summary>Accepts an owned text presentation and optional borrowed asset-texture loader for headless verification.</summary>
    public BodyTemperatureThermometerPresentation(HudOverlayIconTextPresentation text,
        System.Func<HudOverlayPreparationContext, LoadedTexture[]>? createIcons = null)
    {
        this.text = text;
        this.createIcons = createIcons ?? LoadIcons;
    }
    /// <summary>Updates raw fill and color while the text cache changes only when the whole-degree value changes.</summary>
    public void SetContent(BodyTemperatureSample value, bool showReading)
    {
        sample = value;
        this.showReading = showReading;
        if (showReading) text.SetContent(null, "vanillaexpanded:body-temperature-reading", value.DisplayCelsius);
        else text.Reset();
        // Retain one tint object so unchanged render frames allocate no colors or artwork.
        (tint.R, tint.G, tint.B) = value.Risk switch
        {
            BodyTemperatureRisk.Cold or BodyTemperatureRisk.Freezing =>
                (.6f - .35f * value.FreezingStrength, .85f - .25f * value.FreezingStrength, 1f),
            BodyTemperatureRisk.Hot => (1f, .65f, .2f),
            BodyTemperatureRisk.Overheating => (1f, .25f, .15f),
            _ => (.6f, .85f, 1f)
        };
    }
    /// <summary>Loads borrowed artwork only when API ownership changes and prepares optional localized text.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (sample == null) return;
        if (icons == null || !ReferenceEquals(context?.Api, preparation.Api))
        {
            var replacement = createIcons(preparation);
            icons = replacement;
        }
        context = preparation;
        if (showReading) text.Prepare(preparation);
        Size = new SizeF(IconWidth + (showReading ? Gap + text.Size.Width : 0), Math.Max(IconHeight + (sample.Trend != 0 ? 22 : 0), showReading ? text.Size.Height : 0));
    }
    /// <summary>Draws the tinted full mask through a fill scissor, then the outline through native texture rendering within the host clip.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        if (sample == null || icons == null || context == null) return;
        double scale = context.GuiScale;
        double x = bounds.renderX, y = bounds.renderY + (bounds.OuterHeight - IconHeight * scale) / 2;
        // Reveal the full mask upward while retaining the bulb at the minimum temperature fill.
        double fillTop = y + 15.5 * (1 - sample.Fill) * scale;
        double left = Math.Max(x, clip.Left), top = Math.Max(fillTop, clip.Top);
        double right = Math.Min(x + IconWidth * scale, clip.Right), bottom = Math.Min(y + IconHeight * scale, clip.Bottom);
        if (right > left && bottom > top)
        {
            fillClip.absFixedX = left; fillClip.absFixedY = top;
            fillClip.absInnerWidth = right - left; fillClip.absInnerHeight = bottom - top;
            fillClip.Initialized = true;
            renderer.PushScissor(fillClip, true);
            try { renderer.RenderTexture(icons[1].TextureId, x, y, IconWidth * scale, IconHeight * scale, 50, tint); }
            finally { renderer.PopScissor(); }
        }
        renderer.RenderTexture(icons[0].TextureId, x, y, IconWidth * scale, IconHeight * scale, 50);
        if (sample.Trend != 0)
        {
            // Reserve both ends in measurement so reversing direction keeps the thermometer stationary.
            bool warming = sample.Trend > 0;
            for (int index = 0; index < Math.Abs(sample.Trend); index++)
            {
                double arrowY = warming ? y - (6 + index * 5) * scale : y + (IconHeight + 2 + index * 5) * scale;
                renderer.RenderTexture(icons[warming ? 2 : 3].TextureId, x + 3 * scale, arrowY,
                    8 * scale, 4 * scale, 50, warming ? warmingTint : coolingTint);
            }
        }
        if (showReading)
        {
            // Use an independent cached bounds object only for the text's native draw coordinates.
            textBounds.absFixedX = x + (IconWidth + Gap) * scale;
            textBounds.absFixedY = bounds.renderY;
            textBounds.absInnerWidth = text.Size.Width * scale;
            textBounds.absInnerHeight = bounds.OuterHeight;
            textBounds.Initialized = true;
            text.Draw(renderer, textBounds, clip, deltaTime);
        }
    }
    #endregion
    #region Lifecycle
    /// <summary>Releases owned text and borrowed texture bindings and clears sampled state at suspension or world exit.</summary>
    public void Reset()
    {
        icons = null;
        text.Reset();
        sample = null;
        context = null;
        Size = SizeF.Empty;
    }
    /// <summary>Releases borrowed artwork bindings and the owned text presentation on final removal.</summary>
    public void Dispose()
    {
        Reset();
        text.Dispose();
    }
    #endregion
    #endregion

    #region Private
    private readonly ElementBounds textBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    /// <summary>Loads borrowed texture wrappers through the engine cache without taking disposal ownership.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        var outline = new LoadedTexture(preparation.Api);
        var mask = new LoadedTexture(preparation.Api);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/outline.png"), ref outline);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/fill-mask.png"), ref mask);
        // The engine cache owns these textures; wrapper finalizers must not report intentional borrowing as a leak.
        outline.IgnoreUndisposed = true;
        mask.IgnoreUndisposed = true;
        var up = new LoadedTexture(preparation.Api);
        var down = new LoadedTexture(preparation.Api);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/chevron-up.png"), ref up);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/chevron-down.png"), ref down);
        up.IgnoreUndisposed = true;
        down.IgnoreUndisposed = true;
        return [outline, mask, up, down];
    }
    #endregion
}
