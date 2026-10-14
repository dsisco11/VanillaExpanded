using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Borrows cached time-of-day textures and draws a copper gauge over a rotating day/night disk.</summary>
internal sealed class ApproximateTimePresentation : IDisposable
{
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IApproximateTimeDialRenderer> createDial;
    private LoadedTexture[]? icons;
    private IApproximateTimeDialRenderer? dial;
    private HudOverlayPreparationContext? context;
    private ApproximateTimePeriod? period;
    private float rotation;
    private float? temperature;
    private readonly HudOverlayIconTextPresentation reading;
    private readonly ElementBounds readingBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    /// <summary>Controls visibility and retained resources during transitions.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    public SizeF Size { get; private set; }

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public ApproximateTimePresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IApproximateTimeDialRenderer>? createDial = null,
        HudOverlayIconTextPresentation? reading = null)
    {
        this.reading = reading ?? new HudOverlayIconTextPresentation(fontSize: HudOverlaySizes.Text);
        this.load = load ?? LoadIcons;
        this.createDial = createDial ?? ApproximateTimeDialRenderer.Create;
    }
    /// <summary>Stores the estimated period independently of the current native hour.</summary>
    public void SetContent(ApproximateTimePeriod value, double estimatedDay)
    {
        period = value;
        rotation = (float)((estimatedDay - Math.Floor(estimatedDay) - .5) * 2 * Math.PI);
    }
    /// <summary>Updates the environmental reading independently of approximate-clock drift.</summary>
    public void SetTemperature(float? value)
    {
        temperature = value;
        if (value.HasValue)
        {
            double rounded = Math.Round(value.Value, MidpointRounding.AwayFromZero);
            reading.SetContent(null, "vanillaexpanded:environmental-temperature-reading", rounded == 0 ? 0d : rounded);
        }
        else reading.Reset();
    }
    /// <summary>Loads borrowed textures once per client API and reserves the shared extra-large icon slot.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!period.HasValue) return;
        if (icons == null || !ReferenceEquals(context?.Api, preparation.Api)) icons = load(preparation);
        if (dial == null || !ReferenceEquals(context?.Api, preparation.Api))
        {
            dial?.Dispose();
            dial = createDial(preparation);
        }
        if (temperature.HasValue) reading.Prepare(preparation);
        context = preparation;
        Size = new SizeF(HudOverlaySizes.ExtraLarge, HudOverlaySizes.ExtraLarge);
        Visibility.Show();
    }
    /// <summary>Draws prepared artwork without time or terrain sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!period.HasValue || icons == null || context == null) return;
        double pixels = HudOverlaySizes.ExtraLarge * context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        dial?.Draw(renderer, icons[1].TextureId,
            new RectangleF((float)bounds.renderX, (float)y, (float)pixels, (float)pixels),
            HudOverlayDrawOpacity.Current, rotation);
        renderer.RenderTexture(icons[temperature.HasValue ? 2 : 0].TextureId, bounds.renderX,
            bounds.renderY + (bounds.OuterHeight - pixels) / 2, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
        if (temperature.HasValue)
        {
            // Liquid and number share the fixed housing while scenery rotates independently.
            dial?.DrawTemperature(renderer, new RectangleF((float)bounds.renderX, (float)y, (float)pixels, (float)pixels),
                HudOverlayDrawOpacity.Current, Math.Clamp((temperature.Value + 20) / 60, 0, 1));
            double width = Math.Min(reading.Size.Width, 36);
            double height = Math.Min(reading.Size.Height, 16);
            readingBounds.absFixedX = bounds.renderX + (HudOverlaySizes.ExtraLarge - width) * context.GuiScale / 2;
            readingBounds.absFixedY = y + 40 * context.GuiScale;
            readingBounds.absInnerWidth = width * context.GuiScale;
            readingBounds.absInnerHeight = height * context.GuiScale;
            readingBounds.Initialized = true;
            reading.Draw(renderer, readingBounds, new RectangleF((float)bounds.renderX, (float)y, (float)pixels, (float)pixels),
                0, maximumTextHeight: 16);
        }
    }
    #endregion
    #region Lifetime
    /// <summary>Retains prepared content until the visibility exit finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Drops session bindings without disposing engine-owned cached textures.</summary>
    public void Reset()
    {
        Visibility.Reset();
        reading.Reset();
        temperature = null;
        icons = null;
        dial?.Dispose();
        dial = null;
        context = null;
        period = null;
        Size = SizeF.Empty;
    }
    /// <summary>Releases borrowed bindings on final removal.</summary>
    public void Dispose()
    {
        Reset();
        reading.Dispose();
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Loads copper housing and scenery disk assets with engine cache ownership.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        string[] names = ["housing", "disk", "thermometer-housing"];
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/approximate-time/" + names[index] + ".png"), ref texture);
            HudOverlayTextureFiltering.Prepare(texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion
}
