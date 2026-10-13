using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Owns a cached Celsius reading and gradient renderer above borrowed scale-mask and marker textures.</summary>
internal sealed class EnvironmentalTemperaturePresentation : IDisposable
{
    private const float MinimumCelsius = -20, MaximumCelsius = 40, ReadingHeight = HudOverlaySizes.EnvironmentalReadingHeight, ScaleWidth = HudOverlaySizes.TemperatureScaleWidth;
    private readonly HudOverlayIconTextPresentation text;
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> loadTextures;
    private readonly System.Func<HudOverlayPreparationContext, IEnvironmentalTemperatureScaleRenderer> createScaleRenderer;
    private IEnvironmentalTemperatureScaleRenderer? scaleRenderer;
    private readonly ElementBounds textBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private LoadedTexture[]? textures;
    private HudOverlayPreparationContext? context;
    private float readingWidth;

    #region Public API
    /// <summary>Gets the shared fade transition and retained presentation lifetime.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Gets the bounded marker position; the numeric reading remains unclamped.</summary>
    internal float MarkerPosition { get; private set; }
    /// <summary>Gets the base layout footprint before native GUI scaling.</summary>
    public SizeF Size { get; private set; }

    #region Content and preparation
    /// <summary>Accepts optional owned text presentation and borrowed texture loader for headless validation.</summary>
    public EnvironmentalTemperaturePresentation(HudOverlayIconTextPresentation? text = null,
        System.Func<HudOverlayPreparationContext, LoadedTexture[]>? loadTextures = null,
        System.Func<HudOverlayPreparationContext, IEnvironmentalTemperatureScaleRenderer>? createScaleRenderer = null)
    {
        this.text = text ?? new HudOverlayIconTextPresentation(CreateReadingText);
        this.loadTextures = loadTextures ?? LoadTextures;
        this.createScaleRenderer = createScaleRenderer ?? EnvironmentalTemperatureScaleRenderer.Create;
    }

    /// <summary>Updates continuous marker position while caching text at whole-degree precision.</summary>
    public void SetContent(float celsius)
    {
        // Only the visual scale saturates; temperatures outside its range remain readable numerically.
        MarkerPosition = Math.Clamp((celsius - MinimumCelsius) / (MaximumCelsius - MinimumCelsius), 0, 1);
        double reading = Math.Round(celsius, MidpointRounding.AwayFromZero);
        text.SetContent(null, "vanillaexpanded:environmental-temperature-reading", reading == 0 ? 0d : reading);
    }

    /// <summary>Loads cached assets and prepares the reading before measurement or drawing.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (textures == null || !ReferenceEquals(context?.Api, preparation.Api)) textures = loadTextures(preparation);
        if (scaleRenderer == null || !ReferenceEquals(context?.Api, preparation.Api))
        {
            var replacement = createScaleRenderer(preparation);
            scaleRenderer?.Dispose();
            scaleRenderer = replacement;
        }
        text.Prepare(preparation);
        context = preparation;
        readingWidth = text.Size.Width * Math.Min(1, ReadingHeight / Math.Max(1, text.Size.Height));
        Size = new SizeF(Math.Max(HudOverlaySizes.Wide, readingWidth), HudOverlaySizes.Standard);
        Visibility.Show();
    }
    #endregion

    #region Rendering
    /// <summary>Draws the cached number and a marker constrained to the scale beneath it.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip)
    {
        if (context == null || textures == null) return;
        double scale = context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - HudOverlaySizes.Standard * scale) / 2;
        double center = bounds.renderX + Size.Width * scale / 2;
        textBounds.absFixedX = center - readingWidth * scale / 2;
        textBounds.absFixedY = y;
        textBounds.absInnerWidth = readingWidth * scale;
        textBounds.absInnerHeight = ReadingHeight * scale;
        textBounds.Initialized = true;
        text.Draw(renderer, textBounds, clip, 0, maximumTextHeight: ReadingHeight);
        // Endpoint marker centers remain inside the reserved footprint, including their contrast border.
        double left = center - ScaleWidth * scale / 2;
        scaleRenderer!.Draw(renderer, textures[0].TextureId,
            new RectangleF((float)left, (float)(y + 28.5 * scale), (float)(ScaleWidth * scale), (float)(3 * scale)), HudOverlayDrawOpacity.Current);
        renderer.RenderTexture(textures[1].TextureId, left + (ScaleWidth * MarkerPosition - 3.75) * scale,
            y + 26.25 * scale, 7.5 * scale, 7.5 * scale, 51, HudOverlayDrawOpacity.Tint);
    }
    #endregion

    #region Lifecycle
    /// <summary>Retains the last valid reading and assets until the fade completes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Releases owned text and gradient resources plus borrowed texture bindings at suspension or world exit.</summary>
    public void Reset()
    {
        Visibility.Reset();
        text.Reset();
        scaleRenderer?.Dispose();
        scaleRenderer = null;
        textures = null;
        context = null;
        readingWidth = 0;
        MarkerPosition = 0;
        Size = SizeF.Empty;
    }
    /// <summary>Releases the owned presentation without disposing engine-cached assets.</summary>
    public void Dispose()
    {
        Reset();
        text.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates normal HUD text using native GUI scaling using the shared font size.</summary>
    private static LoadedTexture CreateReadingText(HudOverlayPreparationContext preparation, string localized)
    {
        var font = CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text)
            .WithStroke(new double[] { 0, 0, 0, .85 }, 1.5);
        return preparation.Api.Gui.TextTexture.GenTextTexture(localized, font);
    }
    /// <summary>Borrows the authored scale and marker PNGs through the engine texture cache.</summary>
    private static LoadedTexture[] LoadTextures(HudOverlayPreparationContext preparation)
    {
        string[] names = ["scale", "marker"];
        var result = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/environmental-temperature/" + names[index] + ".png"), ref texture);
            texture.IgnoreUndisposed = true;
            result[index] = texture;
        }
        return result;
    }
    #endregion
}
