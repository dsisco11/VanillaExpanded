using System;
using System.Drawing;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.CalendarDate;

/// <summary>Retains sampled date lettering and dimensions while native elements own the roller artwork.</summary>
internal sealed class CalendarDatePresentation : InstrumentPresentation
{
    #region Public API
    /// <summary>Gets the authored canvas used by housing and registered apertures.</summary>
    internal static readonly SizeF Canvas = new(512, 192);
    /// <summary>Gets the registered month and day apertures in authored coordinates.</summary>
    internal static readonly RectangleF[] Windows = [new(44, 34, 254, 124), new(324, 34, 144, 124)];
    /// <summary>Gets the sampled native date.</summary>
    internal CalendarDateReading? Date { get; private set; }
    /// <summary>Gets prepared month lettering.</summary>
    internal string Month { get; private set; } = string.Empty;
    /// <summary>Gets prepared day lettering.</summary>
    internal string Day { get; private set; } = string.Empty;
    /// <summary>Accepts CPU metrics and independent native resource factories.</summary>
    public CalendarDatePresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null) : base(measure, createText, load) { }
    /// <summary>Stores the native date without querying the world during presentation.</summary>
    public void SetContent(CalendarDateReading value) => Date = value;
    /// <summary>Prepares localized strings and canvas dimensions before packing.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Date is not CalendarDateReading value) return;
        Month = value.MonthAbbreviation(context.Locale); Day = value.Day.ToString(CultureInfo.InvariantCulture);
        if (PrepareSimple(context, "date", Month + " " + Day, "textures/hud/calendar-date/icon.svg")) return;
        Size = new(HudOverlaySizes.Calendar, HudOverlaySizes.Calendar * Canvas.Height / Canvas.Width); Visibility.Show();
    }
    /// <summary>Creates composer-owned native roller windows or the selected simple style.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == EnvironmentalGaugeStyle.Immersive
        ? new CalendarView(api, this, Load, CreateText) : CreateSimpleView(api);
    #endregion

    #region Protected API
    /// <summary>Clears native date data after exit or world teardown.</summary>
    protected override void ClearContent() { Date = null; Month = Day = string.Empty; }
    #endregion
}
