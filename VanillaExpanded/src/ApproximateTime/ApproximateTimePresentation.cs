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
    /// <summary>Controls visibility and retained resources during transitions.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    public SizeF Size { get; private set; }

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public ApproximateTimePresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IApproximateTimeDialRenderer>? createDial = null)
    {
        this.load = load ?? LoadIcons;
        this.createDial = createDial ?? ApproximateTimeDialRenderer.Create;
    }
    /// <summary>Stores the estimated period independently of the current native hour.</summary>
    public void SetContent(ApproximateTimePeriod value, double estimatedDay)
    {
        period = value;
        rotation = (float)((estimatedDay - Math.Floor(estimatedDay) - .5) * 2 * Math.PI);
    }
    /// <summary>Loads borrowed textures once per client API and reserves a fixed 24-unit icon slot.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!period.HasValue) return;
        if (icons == null || !ReferenceEquals(context?.Api, preparation.Api)) icons = load(preparation);
        if (dial == null || !ReferenceEquals(context?.Api, preparation.Api))
        {
            dial?.Dispose();
            dial = createDial(preparation);
        }
        context = preparation;
        Size = new SizeF(24, 24);
        Visibility.Show();
    }
    /// <summary>Draws prepared artwork without time or terrain sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!period.HasValue || icons == null || context == null) return;
        double pixels = 24 * context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        dial?.Draw(renderer, icons[1].TextureId,
            new RectangleF((float)bounds.renderX, (float)y, (float)pixels, (float)pixels),
            HudOverlayDrawOpacity.Current, rotation);
        renderer.RenderTexture(icons[0].TextureId, bounds.renderX,
            bounds.renderY + (bounds.OuterHeight - pixels) / 2, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
    }
    #endregion
    #region Lifetime
    /// <summary>Retains prepared content until the visibility exit finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Drops session bindings without disposing engine-owned cached textures.</summary>
    public void Reset()
    {
        Visibility.Reset();
        icons = null;
        dial?.Dispose();
        dial = null;
        context = null;
        period = null;
        Size = SizeF.Empty;
    }
    /// <summary>Releases borrowed bindings on final removal.</summary>
    public void Dispose() => Reset();
    #endregion
    #endregion
    #region Private
    /// <summary>Loads copper housing and scenery disk assets with engine cache ownership.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        string[] names = ["housing", "disk"];
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/approximate-time/" + names[index] + ".png"), ref texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion
}
