using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Composes a fixed solar dial, an approximate-time pointer, and a glass foreground.</summary>
internal sealed class ApproximateTimePresentation : IDisposable
{
    private readonly Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly IApproximateTimeDialRenderer dial;
    private readonly Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture> createFace;
    private LoadedTexture[]? icons;
    private LoadedTexture? face;
    private HudOverlayPreparationContext? context;
    private ApproximateTimeDayPhases phases = ApproximateTimeDayPhases.Neutral;
    private bool faceDirty = true;
    private ApproximateTimePeriod? period;
    private float rotation;
    private readonly HudOverlayIconReading compact;
    private EnvironmentalGaugeStyle style = EnvironmentalGaugeStyle.Immersive;

    #region Public API
    /// <summary>Controls visibility and retained resources during transitions.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Gets the square footprint from the shared palette.</summary>
    public SizeF Size { get; private set; }

    #region Content and rendering
    /// <summary>Uses borrowed artwork, an owned face, and a stateless pointer renderer with injectable headless dependencies.</summary>
    public ApproximateTimePresentation(Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        IApproximateTimeDialRenderer? dial = null,
        Func<HudOverlayPreparationContext, ApproximateTimeDayPhases, LoadedTexture>? createFace = null,
        HudOverlayIconReading? compact = null)
    {
        this.load = load ?? (preparation => WorkshopGaugeArtwork.Load(preparation,
            RadialGaugeLayout.Backing, "clock/layer-1", RadialGaugeLayout.Glass));
        this.dial = dial ?? new ApproximateTimeDialRenderer();
        this.createFace = createFace ?? ApproximateTimeFaceArtwork.Create;
        this.compact = compact ?? new();
    }

    /// <summary>Changes presentation without discarding the synchronized time estimate or solar distribution.</summary>
    public bool SetStyle(EnvironmentalGaugeStyle value)
    {
        EnvironmentalGaugeStyle next = value == EnvironmentalGaugeStyle.Icon ? value : EnvironmentalGaugeStyle.Immersive;
        if (next == style) return false;
        compact.Reset();
        icons = null; face?.Dispose(); face = null; faceDirty = true;
        context = null; Size = SizeF.Empty; style = next;
        return true;
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
        if (style == EnvironmentalGaugeStyle.Icon)
        {
            compact.Prepare(preparation, "textures/hud/approximate-time/" + period.Value.ToString().ToLowerInvariant() + ".png");
            context = preparation; Size = compact.Size; Visibility.Show();
            return;
        }
        bool apiChanged = !ReferenceEquals(context?.Api, preparation.Api);
        if (icons == null || apiChanged) icons = load(preparation);
        if (face == null || faceDirty || apiChanged)
        {
            var replacement = createFace(preparation, phases);
            face?.Dispose();
            face = replacement;
            faceDirty = false;
        }
        context = preparation;
        Size = new SizeF(HudOverlaySizes.Clock, HudOverlaySizes.Clock);
        Visibility.Show();
    }

    /// <summary>Draws the backing, colored face, rotated pointer, and glass in sandwich order.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!period.HasValue || context == null) return;
        if (style == EnvironmentalGaugeStyle.Icon) { compact.Draw(renderer, bounds); return; }
        if (icons == null || face == null) return;
        float pixels = (float)(HudOverlaySizes.Clock * context.GuiScale);
        var screen = new RectangleF((float)bounds.renderX, (float)(bounds.renderY + (bounds.OuterHeight - pixels) / 2), pixels, pixels);
        renderer.RenderTexture(icons[0].TextureId, screen.X, screen.Y, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
        renderer.Render2DTexturePremultipliedAlpha(face.TextureId, screen.X, screen.Y, pixels, pixels, 51, HudOverlayDrawOpacity.PremultipliedTint);
        dial.Draw(renderer, icons[1].TextureId, screen, HudOverlayDrawOpacity.Current, rotation);
        renderer.RenderTexture(icons[2].TextureId, screen.X, screen.Y, pixels, pixels, 53, HudOverlayDrawOpacity.Tint);
    }

    #endregion
    #region Lifetime
    /// <summary>Retains prepared content until the visibility exit finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Releases owned resources and drops borrowed bindings at world exit or completed fade.</summary>
    public void Reset()
    {
        Visibility.Reset();
        compact.Reset();
        icons = null;
        face?.Dispose(); face = null;
        context = null;
        period = null;
        phases = ApproximateTimeDayPhases.Neutral;
        faceDirty = true;
        Size = SizeF.Empty;
    }
    /// <summary>Releases presentation ownership on final removal.</summary>
    public void Dispose()
    {
        Reset();
        compact.Dispose();
    }
    #endregion
    #endregion
}
