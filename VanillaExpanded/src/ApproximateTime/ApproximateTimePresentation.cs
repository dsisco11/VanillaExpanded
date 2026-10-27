using System;
using System.Drawing;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Retains estimated pointer state and actual clock text without owning GUI resources.</summary>
internal sealed class ApproximateTimePresentation : InstrumentPresentation
{
    private readonly Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture>? createFace;

    #region Public API
    /// <summary>Gets the sampled daily solar distribution.</summary>
    internal ApproximateTimeDayPhases Phases { get; private set; } = ApproximateTimeDayPhases.Neutral;
    /// <summary>Gets the estimated pointer angle independently of actual clock text.</summary>
    internal float Rotation { get; private set; }
    /// <summary>Gets the sampled time period.</summary>
    internal ApproximateTimePeriod? Period { get; private set; }
    /// <summary>Gets actual calendar time formatted for text style.</summary>
    internal string ClockText { get; private set; } = string.Empty;
    /// <summary>Accepts independent native resource factories for each composition.</summary>
    public ApproximateTimePresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture>? createFace = null) : base(measure, createText, load)
    {
        this.createFace = createFace;
    }
    /// <summary>Updates the estimated hand, actual calendar text and independently sampled solar distribution.</summary>
    public bool SetContent(ApproximateTimePeriod value, double estimatedDay, ApproximateTimeDayPhases? distribution = null, double? actualHour = null)
    {
        string previous = ClockText;
        Period = value;
        Rotation = (float)((estimatedDay - Math.Floor(estimatedDay) - .5) * 2 * Math.PI);
        double hour = actualHour ?? (estimatedDay - Math.Floor(estimatedDay)) * 24;
        // Clock lettering is authoritative calendar time; only the hand follows observation drift.
        if (double.IsFinite(hour))
        {
            int minutes = (int)Math.Floor((hour - Math.Floor(hour / 24) * 24) * 60);
            ClockText = string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
        }
        if (distribution != null) Phases = distribution;
        return Style == EnvironmentalGaugeStyle.Text && previous != ClockText;
    }
    /// <summary>Publishes canonical dimensions and localized data before container packing.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (!Period.HasValue) return;
        if (PrepareSimple(context, "time", ClockText,
            "textures/hud/approximate-time/" + Period.Value.ToString().ToLowerInvariant() + ".png", false)) return;
        Size = new(HudOverlaySizes.Clock, HudOverlaySizes.Clock); Visibility.Show();
    }
    /// <summary>Creates independently owned native layers for the current style.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == EnvironmentalGaugeStyle.Immersive
        ? new ClockView(api, this, Load, createFace) : CreateSimpleView(api);
    #endregion

    #region Protected API
    /// <summary>Clears sampled state after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        Period = null; Phases = ApproximateTimeDayPhases.Neutral; ClockText = string.Empty; Rotation = 0;
    }
    #endregion
}
