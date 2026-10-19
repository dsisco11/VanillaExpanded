using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Composes a fixed solar dial, an approximate-time pointer, and a glass foreground.</summary>
internal sealed class ApproximateTimePresentation : HudIndicatorPresentation<EnvironmentalGaugeStyle>
{
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly IApproximateTimeDialRenderer dial;
    private readonly Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture> createFace;
    private readonly ApproximateTimeFaceArtwork? artwork;
    private LoadedTexture[]? icons;
    private LoadedTexture? face;
    private ApproximateTimeDayPhases phases = ApproximateTimeDayPhases.Neutral;
    private bool faceDirty = true;
    private ApproximateTimePeriod? period;
    private float rotation;
    private readonly HudOverlayIconReading compact;

    #region Public API

    #region Content and rendering
    /// <summary>Uses borrowed artwork, an owned face, and a stateless pointer renderer with injectable headless dependencies.</summary>
    public ApproximateTimePresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        IApproximateTimeDialRenderer? dial = null,
        Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture>? createFace = null,
        HudOverlayIconReading? compact = null, HudIndicatorTextPresentation? textStyle = null)
        : base(EnvironmentalGaugeStyle.Immersive, EnvironmentalGaugeStyle.Text, textStyle)
    {
        this.load = load ?? (preparation => WorkshopGaugeArtwork.Load(preparation,
            "clock/layer-1", RadialGaugeLayout.Glass));
        this.dial = dial ?? new ApproximateTimeDialRenderer();
        if (createFace == null) artwork = new ApproximateTimeFaceArtwork();
        this.createFace = createFace ?? artwork!.Create;
        this.compact = compact ?? new();
    }


    /// <summary>Updates the drifting pointer independently of the true daily solar distribution.</summary>
    public void SetContent(ApproximateTimePeriod value, double estimatedDay, ApproximateTimeDayPhases? distribution = null)
    {
        period = value;
        rotation = (float)((estimatedDay - Math.Floor(estimatedDay) - .5) * 2 * Math.PI);
        if (distribution != null && !phases.Equals(distribution))
        {
            phases = distribution;
            faceDirty = true;
        }
    }

    /// <summary>Prepares only changed face pixels and reserves the complete registered canvas.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!period.HasValue) return;
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "time", period.Value.ToString().ToUpperInvariant());
            return;
        }
        if (Style == EnvironmentalGaugeStyle.Icon)
        {
            compact.Prepare(preparation, "textures/hud/approximate-time/" + period.Value.ToString().ToLowerInvariant() + ".png");
            Context = preparation; Size = compact.Size; Visibility.Show();
            return;
        }
        bool apiChanged = !ReferenceEquals(Context?.Api, preparation.Api);
        if (icons == null || apiChanged) icons = load(preparation);
        if (face == null || faceDirty || apiChanged || artwork?.Dirty == true)
        {
            var replacement = createFace(preparation, phases);
            if (artwork == null && !ReferenceEquals(face, replacement)) face?.Dispose();
            face = replacement;
            faceDirty = false;
        }
        Context = preparation;
        Size = new SizeF(HudOverlaySizes.Clock, HudOverlaySizes.Clock);
        Visibility.Show();
    }

    /// <summary>Draws the cached background, rotated pointer, and glass in sandwich order.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!period.HasValue || Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (Style == EnvironmentalGaugeStyle.Icon) { compact.Draw(renderer, bounds); return; }
        if (icons == null || face == null) return;
        float pixels = (float)(HudOverlaySizes.Clock * Context.GuiScale);
        var screen = new RectangleF((float)bounds.renderX, (float)(bounds.renderY + (bounds.OuterHeight - pixels) / 2), pixels, pixels);
        renderer.Render2DTexturePremultipliedAlpha(face.TextureId, screen.X, screen.Y, pixels, pixels, 51, HudOverlayDrawOpacity.PremultipliedTint);
        dial.Draw(renderer, icons[0].TextureId, screen, HudOverlayDrawOpacity.Current, rotation);
        renderer.RenderTexture(icons[1].TextureId, screen.X, screen.Y, pixels, pixels, 53, HudOverlayDrawOpacity.Tint);
    }

    #endregion
    #region Lifetime
    /// <summary>Ends shared presentation ownership and disposes feature-owned helpers.</summary>
    public override void Dispose()
    {
        base.Dispose();
        compact.Dispose();
    }
    #endregion
    #endregion


    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        compact.Reset(); icons = null;
        if (artwork != null) artwork.Reset();
        else face?.Dispose();
        face = null; faceDirty = true;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        period = null; phases = ApproximateTimeDayPhases.Neutral;
    }
    #endregion
}
