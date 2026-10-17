using System;
using System.Drawing;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.CalendarDate;

/// <summary>Composes native date characters between a blank mechanical roller housing and its glass.</summary>
internal sealed class CalendarDatePresentation : IDisposable
{
    private static readonly SizeF Canvas = new(512, 192);
    private static readonly RectangleF[] Windows = [new(44, 34, 254, 124), new(324, 34, 144, 124)];
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly WorkshopGaugeText[] characters;
    private LoadedTexture[]? textures;
    private HudOverlayPreparationContext? context;
    private CalendarDateReading? date;
    private readonly HudOverlayIconReading compact;
    private EnvironmentalGaugeStyle style = EnvironmentalGaugeStyle.Immersive;

    #region Public API
    /// <summary>Gets the standard fade and collapse transition.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Gets the complete canvas footprint in unscaled GUI units.</summary>
    public SizeF Size { get; private set; }

    #region Content and rendering
    /// <summary>Accepts borrowed artwork and owned text factories for headless validation.</summary>
    public CalendarDatePresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        Func<WorkshopGaugeText>? createText = null, HudOverlayIconReading? compact = null)
    {
        this.load = load ?? (preparation => WorkshopGaugeArtwork.Load(preparation, "calendar/layer-0", "calendar/layer-2"));
        createText ??= () => new WorkshopGaugeText();
        characters = [createText(), createText()];
        this.compact = compact ?? new();
    }

    /// <summary>Changes the instrument style while retaining the native date and active visibility transition.</summary>
    public bool SetStyle(EnvironmentalGaugeStyle value)
    {
        EnvironmentalGaugeStyle next = value == EnvironmentalGaugeStyle.Icon ? value : EnvironmentalGaugeStyle.Immersive;
        if (next == style) return false;
        compact.Reset();
        foreach (var text in characters) text.Reset();
        textures = null; context = null; Size = SizeF.Empty; style = next;
        return true;
    }

    /// <summary>Stores sampled native date content without querying the world during drawing.</summary>
    public void SetContent(CalendarDateReading value) => date = value;

    /// <summary>Prepares localized month lettering and cached number textures before layout.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (date is not CalendarDateReading value) return;
        if (style == EnvironmentalGaugeStyle.Icon)
        {
            compact.Prepare(preparation, "textures/hud/calendar-date/icon.svg",
                value.MonthAbbreviation(preparation.Locale) + " " + value.Day.ToString(CultureInfo.InvariantCulture));
            context = preparation; Size = compact.Size; Visibility.Show();
            return;
        }
        if (textures == null || !ReferenceEquals(context?.Api, preparation.Api)) textures = load(preparation);
        characters[0].SetContent(value.MonthAbbreviation(preparation.Locale));
        characters[1].SetContent(value.Day.ToString(CultureInfo.InvariantCulture));
        foreach (var text in characters) text.Prepare(preparation);
        context = preparation;
        Size = new(HudOverlaySizes.Calendar, HudOverlaySizes.Calendar * Canvas.Height / Canvas.Width);
        Visibility.Show();
    }

    /// <summary>Draws all characters inside the registered roller apertures before the glass foreground.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (context == null) return;
        if (style == EnvironmentalGaugeStyle.Icon) { compact.Draw(renderer, bounds); return; }
        if (textures == null) return;
        float width = (float)(HudOverlaySizes.Calendar * context.GuiScale), height = width * Canvas.Height / Canvas.Width;
        var screen = new RectangleF((float)bounds.renderX, (float)(bounds.renderY + (bounds.OuterHeight - height) / 2), width, height);
        renderer.RenderTexture(textures[0].TextureId, screen.X, screen.Y, width, height, 50, HudOverlayDrawOpacity.Tint);
        for (int index = 0; index < characters.Length; index++)
            characters[index].Draw(renderer, WorkshopGaugeArtwork.Map(screen, Canvas, Windows[index]), 51);
        renderer.RenderTexture(textures[1].TextureId, screen.X, screen.Y, width, height, 52, HudOverlayDrawOpacity.Tint);
    }

    #endregion
    #region Lifetime
    /// <summary>Retains prepared artwork until the standard exit completes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Releases owned lettering while dropping the engine-owned artwork bindings.</summary>
    public void Reset()
    {
        Visibility.Reset();
        compact.Reset();
        foreach (var text in characters) text.Reset();
        textures = null; context = null; date = null; Size = SizeF.Empty;
    }
    /// <summary>Releases all presentation ownership at final removal.</summary>
    public void Dispose()
    {
        Reset();
        compact.Dispose();
        foreach (var text in characters) text.Dispose();
    }
    #endregion
    #endregion
}
