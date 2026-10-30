using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Composes a narrow thermometer, optional reading and separate trend sprites through native bounds.</summary>
internal sealed class ThermometerView : NativeView
{
    private readonly BodyTemperatureThermometerPresentation content;
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterMiddle);
    private readonly ElementBounds column = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterTop);
    private readonly ElementBounds icon = ElementBounds.Fixed(0, 0, HudOverlaySizes.Small, HudOverlaySizes.Small).WithAlignment(EnumDialogArea.CenterTop);
    private readonly ElementBounds trend = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterTop);
    private readonly ElementBounds readingRow = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.LeftMiddle);
    private readonly TextElement reading;
    private readonly TextureElement fill, outline;
    private readonly TextureElement[] chevrons = new TextureElement[2];
    private (SizeF Size, float Reading, float Column, int Trend)? geometry;
    private double scale, x, y;
    private float opacity;

    #region Public API
    #region Composition and preparation
    /// <summary>Creates unique passive native components without sampling temperature or duplicating subscriptions.</summary>
    public ThermometerView(ICoreClientAPI api, BodyTemperatureThermometerPresentation content,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null) : base(api)
    {
        this.content = content;
        reading = Own(new TextElement(api, ElementBounds.Fixed(0, 0, 0, 0)
            .WithAlignment(EnumDialogArea.CenterMiddle), _ => { }, createText, 1.5));
        try
        {
            fill = Own(new TextureElement(api, ElementBounds.Fixed(0, 0, HudOverlaySizes.ThermometerWidth, HudOverlaySizes.Small)
                .WithAlignment(EnumDialogArea.CenterMiddle), _ => { }, load));
            outline = Own(new TextureElement(api, ElementBounds.Fixed(0, 0, HudOverlaySizes.ThermometerWidth, HudOverlaySizes.Small)
                .WithAlignment(EnumDialogArea.CenterMiddle), _ => { }, load));
            for (int index = 0; index < chevrons.Length; index++)
                chevrons[index] = Own(new TextureElement(api, ElementBounds.Fixed(0, 0, 0, 0)
                    .WithAlignment(EnumDialogArea.CenterTop), _ => { }, load));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates dirty resources and warning colors, arranging native components only when their local geometry changes.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed(); scale = context.GuiScale;
        var sample = content.Sample ?? throw new InvalidOperationException("Prepare temperature content before its native view.");
        reading.Prepare(context, content.Reading);
        fill.Prepare(context, "textures/hud/body-temperature/fill-mask.png");
        outline.Prepare(context, "textures/hud/body-temperature/outline.png");
        var color = sample.Risk switch
        {
            BodyTemperatureRisk.Cold or BodyTemperatureRisk.Freezing => (.6f - .35f * sample.FreezingStrength, .85f - .25f * sample.FreezingStrength, 1f),
            BodyTemperatureRisk.Hot => (1f, .65f, .2f),
            BodyTemperatureRisk.Overheating => (1f, .25f, .15f),
            _ => (.6f, .85f, 1f)
        };
        fill.State.SetColor(color.Item1, color.Item2, color.Item3);
        for (int index = 0; index < chevrons.Length; index++)
        {
            chevrons[index].Prepare(context, index < Math.Abs(sample.Trend)
                ? "textures/hud/body-temperature/chevron-" + (sample.Trend > 0 ? "up" : "down") + ".png" : null);
            if (sample.Trend > 0) chevrons[index].State.SetColor(1, .65f, .2f);
            else chevrons[index].State.SetColor(.25f, .6f, 1);
        }
        var next = (content.Size, content.ReadingWidth, content.ColumnWidth, sample.Trend);
        // Fill and color updates reuse the resolved component tree; only local geometry changes arrange it.
        if (geometry == next) return;
        Arrange(sample.Trend); geometry = next; root.MarkDirtyRecursive();
    }
    /// <summary>Caches text fit, trend targets, fill reveal and backdrop anchor after native bounds resolve.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        ThrowIfDisposed(); reading.LayoutCompleted(clip); outline.LayoutCompleted(clip);
        foreach (var chevron in chevrons) chevron.LayoutCompleted(clip);
        var bounds = fill.Bounds;
        // Reveal the full-size mask upward, retaining its bulb and native sprite target at every fill level.
        float top = (float)(bounds.renderY + 15.5 * (1 - (content.Sample?.Fill ?? 0)) * scale);
        var reveal = new RectangleF((float)bounds.renderX, top, (float)bounds.OuterWidth, (float)(bounds.renderY + bounds.OuterHeight - top));
        var intersection = RectangleF.Intersect(reveal, clip);
        if (intersection.Width <= 0 || intersection.Height <= 0) intersection = RectangleF.Empty;
        fill.LayoutCompleted(intersection);
        x = icon.renderX; y = icon.renderY;
    }
    #endregion
    #region Rendering state
    /// <summary>Shares settled host opacity across mask, outline, markers and lettering.</summary>
    public override void SetOpacity(float value)
    {
        ThrowIfDisposed(); opacity = value; reading.State.SetOpacity(value); fill.State.SetOpacity(value); outline.State.SetOpacity(value);
        foreach (var chevron in chevrons) chevron.State.SetOpacity(value);
    }
    /// <summary>Queues the prepared full-width icon-slot circle rather than the narrow silhouette bounds.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        if (opacity <= 0) return;
        using var scope = new DrawOpacity(opacity); content.Backdrop?.Queue(renderer, x, y, scale * HudOverlaySizes.Small / HudOverlaySizes.Standard);
    }
    /// <summary>Connects all independently drawn components to the owning feature's failure boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        reading.State.Failure = fill.State.Failure = outline.State.Failure = failure;
        foreach (var chevron in chevrons) chevron.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers real native components in fill, outline, markers and reading order.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.BeginChildElements(icon);
            try { composer.AddInteractiveElement(fill, prefix + ":fill"); composer.AddInteractiveElement(outline, prefix + ":outline"); }
            finally { composer.EndChildElements(); }
            composer.BeginChildElements(column);
            try
            {
                composer.BeginChildElements(trend);
                try { for (int index = 0; index < chevrons.Length; index++) composer.AddInteractiveElement(chevrons[index], prefix + ":trend-" + index); }
                finally { composer.EndChildElements(); }
                composer.BeginChildElements(readingRow);
                try { composer.AddInteractiveElement(reading, prefix + ":reading"); }
                finally { composer.EndChildElements(); }
            }
            finally { composer.EndChildElements(); }
        }
        finally { composer.EndChildElements(); }
    }
    #endregion

    #region Private
    /// <summary>Declares native row sizes and alignments while keeping both trend strengths within a stable column.</summary>
    private void Arrange(int direction)
    {
        root.fixedWidth = content.Size.Width; root.fixedHeight = content.Size.Height;
        column.fixedWidth = content.ColumnWidth; column.fixedHeight = content.ColumnWidth > 0 ? HudOverlaySizes.Standard : 0;
        icon.fixedY = content.ColumnWidth > 0 ? HudOverlaySizes.Standard + HudOverlaySizes.ReadingGap : 0;
        reading.Bounds.fixedWidth = content.ReadingWidth; reading.Bounds.fixedHeight = content.ReadingWidth > 0 ? HudOverlaySizes.BodyReadingHeight : 0;
        readingRow.fixedWidth = column.fixedWidth; readingRow.fixedHeight = reading.Bounds.fixedHeight;
        // LeftTop explicitly clears native vertical margins when a row leaves middle/bottom alignment.
        // CenterTop only updates the horizontal margin, so switching to it retains a stale vertical offset.
        readingRow.Alignment = direction > 0 ? EnumDialogArea.LeftBottom : direction < 0 ? EnumDialogArea.LeftTop : EnumDialogArea.LeftMiddle;
        int count = Math.Abs(direction);
        trend.fixedWidth = count > 0 ? column.fixedWidth : 0;
        trend.fixedHeight = count > 0 ? count * HudOverlaySizes.ChevronHeight + (count - 1) * HudOverlaySizes.ChevronGap : 0;
        trend.Alignment = direction > 0 ? EnumDialogArea.LeftTop : EnumDialogArea.LeftBottom;
        for (int index = 0; index < chevrons.Length; index++)
        {
            chevrons[index].Bounds.fixedY = index * (HudOverlaySizes.ChevronHeight + HudOverlaySizes.ChevronGap);
            chevrons[index].Bounds.fixedWidth = index < count ? HudOverlaySizes.ChevronWidth : 0;
            chevrons[index].Bounds.fixedHeight = index < count ? HudOverlaySizes.ChevronHeight : 0;
        }
    }
    #endregion
}
