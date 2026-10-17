using System;
using System.Drawing;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.CalendarDate;

/// <summary>Composes native date characters between a blank mechanical roller housing and its glass.</summary>
internal sealed class CalendarDatePresentation : HudIndicatorPresentation<EnvironmentalGaugeStyle>
{
    private static readonly SizeF Canvas = new(512, 192);
    private static readonly RectangleF[] Windows = [new(44, 34, 254, 124), new(324, 34, 144, 124)];
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly WorkshopGaugeText[] characters;
    private LoadedTexture[]? textures;
    private CalendarDateReading? date;
    private readonly HudOverlayIconReading compact;

    #region Public API

    #region Content and rendering
    /// <summary>Accepts borrowed artwork and owned text factories for headless validation.</summary>
    public CalendarDatePresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        Func<WorkshopGaugeText>? createText = null, HudOverlayIconReading? compact = null,
        HudIndicatorTextPresentation? textStyle = null)
        : base(EnvironmentalGaugeStyle.Immersive, EnvironmentalGaugeStyle.Text, textStyle)
    {
        this.load = load ?? (preparation => WorkshopGaugeArtwork.Load(preparation, "calendar/layer-0", "calendar/layer-2"));
        createText ??= () => new WorkshopGaugeText();
        characters = [createText(), createText()];
        this.compact = compact ?? new();
    }


    /// <summary>Stores sampled native date content without querying the world during drawing.</summary>
    public void SetContent(CalendarDateReading value) => date = value;

    /// <summary>Prepares localized month lettering and cached number textures before layout.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (date is not CalendarDateReading value) return;
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "date", value.MonthAbbreviation(preparation.Locale) + " " + value.Day.ToString(CultureInfo.InvariantCulture));
            return;
        }
        if (Style == EnvironmentalGaugeStyle.Icon)
        {
            compact.Prepare(preparation, "textures/hud/calendar-date/icon.svg",
                value.MonthAbbreviation(preparation.Locale) + " " + value.Day.ToString(CultureInfo.InvariantCulture));
            Context = preparation; Size = compact.Size; Visibility.Show();
            return;
        }
        if (textures == null || !ReferenceEquals(Context?.Api, preparation.Api)) textures = load(preparation);
        characters[0].SetContent(value.MonthAbbreviation(preparation.Locale));
        characters[1].SetContent(value.Day.ToString(CultureInfo.InvariantCulture));
        foreach (var text in characters) text.Prepare(preparation);
        Context = preparation;
        Size = new(HudOverlaySizes.Calendar, HudOverlaySizes.Calendar * Canvas.Height / Canvas.Width);
        Visibility.Show();
    }

    /// <summary>Draws all characters inside the registered roller apertures before the glass foreground.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (Style == EnvironmentalGaugeStyle.Icon) { compact.Draw(renderer, bounds); return; }
        if (textures == null) return;
        float width = (float)(HudOverlaySizes.Calendar * Context.GuiScale), height = width * Canvas.Height / Canvas.Width;
        var screen = new RectangleF((float)bounds.renderX, (float)(bounds.renderY + (bounds.OuterHeight - height) / 2), width, height);
        renderer.RenderTexture(textures[0].TextureId, screen.X, screen.Y, width, height, 50, HudOverlayDrawOpacity.Tint);
        for (int index = 0; index < characters.Length; index++)
            characters[index].Draw(renderer, WorkshopGaugeArtwork.Map(screen, Canvas, Windows[index]), 51);
        renderer.RenderTexture(textures[1].TextureId, screen.X, screen.Y, width, height, 52, HudOverlayDrawOpacity.Tint);
    }

    #endregion
    #region Lifetime
    /// <summary>Ends shared presentation ownership and disposes feature-owned helpers.</summary>
    public override void Dispose()
    {
        base.Dispose();
        compact.Dispose(); foreach (var text in characters) text.Dispose();
    }
    #endregion
    #endregion


    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        compact.Reset(); foreach (var text in characters) text.Reset(); textures = null;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        date = null;
    }
    #endregion
}
