using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Borrows asset-backed thermometer textures and owns whole-degree text; drawing crops and tints a cached full silhouette mask.</summary>
internal sealed class BodyTemperatureThermometerPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    internal const float IconWidth = HudOverlaySizes.ThermometerWidth, IconHeight = HudOverlaySizes.Standard, Gap = HudOverlaySizes.ReadingGap;
    private const float ChevronWidth = HudOverlaySizes.ChevronWidth, ChevronHeight = HudOverlaySizes.ChevronHeight, ChevronGap = HudOverlaySizes.ChevronGap, ReadingHeight = HudOverlaySizes.BodyReadingHeight;
    private readonly HudIndicatorStackLayout layout = new();
    private readonly ElementBounds thermometerBounds = ElementBounds.Fill.WithAlignment(EnumDialogArea.CenterMiddle);
    private readonly ElementBounds textBounds = ElementBounds.Fill.WithAlignment(EnumDialogArea.CenterMiddle);
    private readonly ElementBounds trendBounds = ElementBounds.Fill.WithAlignment(EnumDialogArea.CenterTop);
    private float readingWidth, columnWidth;
    private readonly HudOverlayIconTextPresentation text;
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> createIcons;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private BodyTemperatureSample? sample;
    private bool showReading;
    private readonly ElementBounds fillClip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private readonly Vec4f tint = new(1, 1, 1, 1);
    private readonly Vec4f warmingTint = new(1, .65f, .2f, 1);
    private readonly Vec4f coolingTint = new(.25f, .6f, 1, 1);

    #region Public API
    #region Content and presentation
    /// <summary>Accepts an owned text presentation and optional borrowed asset-texture loader for headless verification.</summary>
    public BodyTemperatureThermometerPresentation(HudOverlayIconTextPresentation text,
        System.Func<HudOverlayPreparationContext, LoadedTexture[]>? createIcons = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudIndicatorTextPresentation? textStyle = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, textStyle)
    {
        this.text = text;
        layout.Icon.WithChild(thermometerBounds);
        layout.Reading.WithChildren(textBounds, trendBounds);
        this.createIcons = createIcons ?? LoadIcons;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
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
        if (IsTextStyle)
        {
            string risk = sample.Risk.ToString().ToUpperInvariant();
            string value = showReading ? Lang.GetL(preparation.Locale, "vanillaexpanded:body-temperature-reading", sample.DisplayCelsius) + " " + risk : risk;
            if (sample.Trend != 0) value += sample.Trend > 0 ? " +" + sample.Trend : " " + sample.Trend;
            TryPrepareText(preparation, "body-temperature", value);
            return;
        }
        if (icons == null || !ReferenceEquals(Context?.Api, preparation.Api))
        {
            var replacement = createIcons(preparation);
            icons = replacement;
        }
        if (backdrop == null || !ReferenceEquals(Context?.Api, preparation.Api)) backdrop = loadBackdrop(preparation);
        Context = preparation;
        if (showReading)
        {
            text.SetContent(null, "vanillaexpanded:body-temperature-reading", sample.DisplayCelsius);
            text.Prepare(preparation);
        }
        // Reserve room for two vertically stacked chevrons and fit the normal-sized reading into the remaining height.
        readingWidth = showReading ? text.Size.Width * Math.Min(1, ReadingHeight / Math.Max(1, text.Size.Height)) : 0;
        float trendWidth = sample.Trend == 0 ? 0 : ChevronWidth;
        columnWidth = Math.Max(readingWidth, trendWidth);
        // Keep the existing reading/chevron column together above the thermometer.
        // Its stable height reserves both trend strengths without moving the thermometer.
        layout.Configure(columnWidth > 0 ? new SizeF(columnWidth, IconHeight) : SizeF.Empty,
            new SizeF(HudOverlaySizes.Standard, IconHeight), Gap);
        thermometerBounds.percentWidth = IconWidth / (double)HudOverlaySizes.Standard;
        textBounds.percentWidth = columnWidth > 0 ? readingWidth / (double)columnWidth : 0;
        textBounds.percentHeight = ReadingHeight / (double)IconHeight;
        textBounds.Alignment = sample.Trend > 0 ? EnumDialogArea.CenterBottom
            : sample.Trend < 0 ? EnumDialogArea.CenterTop : EnumDialogArea.CenterMiddle;
        int count = Math.Abs(sample.Trend);
        trendBounds.percentWidth = columnWidth > 0 ? ChevronWidth / (double)columnWidth : 0;
        trendBounds.percentHeight = count > 0 ? (count * ChevronHeight + (count - 1) * ChevronGap) / (double)IconHeight : 0;
        trendBounds.Alignment = sample.Trend > 0 ? EnumDialogArea.CenterTop : EnumDialogArea.CenterBottom;
        layout.Invalidate();
        Size = layout.Size;
        Visibility.Show();
    }
    /// <summary>Draws the tinted full mask through a fill scissor, then the outline through native texture rendering within the host clip.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        if (sample == null || Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (icons == null) return;
        double scale = Context.GuiScale;
        layout.Resolve(bounds, scale);
        double x = thermometerBounds.renderX, y = thermometerBounds.renderY;
        // Reveal the full mask upward while retaining the bulb at the minimum temperature fill.
        double fillTop = y + 23.25 * (1 - sample.Fill) * scale;
        double left = Math.Max(x, clip.Left), top = Math.Max(fillTop, clip.Top);
        double right = Math.Min(x + IconWidth * scale, clip.Right), bottom = Math.Min(y + IconHeight * scale, clip.Bottom);
        if (right > left && bottom > top)
        {
            fillClip.absFixedX = left; fillClip.absFixedY = top;
            fillClip.absInnerWidth = right - left; fillClip.absInnerHeight = bottom - top;
            fillClip.Initialized = true;
            renderer.PushScissor(fillClip, true);
            try { renderer.RenderTexture(icons[1].TextureId, x, y, IconWidth * scale, IconHeight * scale, 50, HudOverlayDrawOpacity.Multiply(tint)); }
            finally { renderer.PopScissor(); }
        }
        renderer.RenderTexture(icons[0].TextureId, x, y, IconWidth * scale, IconHeight * scale, 50, HudOverlayDrawOpacity.Tint);
        if (sample.Trend != 0)
        {
            // Stack strength vertically in the reading's column; the taller drawing gives the strokes slightly more weight.
            bool warming = sample.Trend > 0;
            double arrowX = trendBounds.renderX, arrowY = trendBounds.renderY;
            for (int index = 0; index < Math.Abs(sample.Trend); index++)
            {
                renderer.RenderTexture(icons[warming ? 2 : 3].TextureId, arrowX, arrowY + index * (ChevronHeight + ChevronGap) * scale,
                    ChevronWidth * scale, ChevronHeight * scale, 50, HudOverlayDrawOpacity.Multiply(warming ? warmingTint : coolingTint));
            }
        }
        if (showReading)
        {
            text.Draw(renderer, textBounds, clip, deltaTime, maximumTextHeight: ReadingHeight);
        }
    }
    #endregion
    /// <summary>Queues the prepared icon circle separately from foreground drawing.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (sample == null || icons == null || Context == null) return;
        layout.Resolve(bounds, Context.GuiScale);
        backdrop?.Queue(renderer, layout.Icon.renderX, layout.Icon.renderY, Context.GuiScale);
    }
    #region Lifecycle
    /// <summary>Ends shared presentation ownership and disposes feature-owned helpers.</summary>
    public override void Dispose()
    {
        base.Dispose();
        text.Dispose();
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        icons = null; backdrop = null; text.Reset();
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        sample = null;
    }
    #endregion

    #region Private
    /// <summary>Loads borrowed texture wrappers through the engine cache without taking disposal ownership.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        var outline = new LoadedTexture(preparation.Api);
        var mask = new LoadedTexture(preparation.Api);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/outline.png"), ref outline);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/fill-mask.png"), ref mask);
        // The engine cache owns these textures; wrapper finalizers must not report intentional borrowing as a leak.
        HudOverlayTextureFiltering.Prepare(outline);
        outline.IgnoreUndisposed = true;
        HudOverlayTextureFiltering.Prepare(mask);
        mask.IgnoreUndisposed = true;
        var up = new LoadedTexture(preparation.Api);
        var down = new LoadedTexture(preparation.Api);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/chevron-up.png"), ref up);
        preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/hud/body-temperature/chevron-down.png"), ref down);
        HudOverlayTextureFiltering.Prepare(up);
        up.IgnoreUndisposed = true;
        HudOverlayTextureFiltering.Prepare(down);
        down.IgnoreUndisposed = true;
        return [outline, mask, up, down];
    }
    #endregion


}
