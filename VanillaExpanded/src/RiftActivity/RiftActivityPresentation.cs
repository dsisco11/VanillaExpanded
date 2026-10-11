using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RiftActivity;

/// <summary>Borrows cached rift activity textures and draws one compact icon without a label or background.</summary>
internal sealed class RiftActivityPresentation : IDisposable
{
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private LoadedTexture[]? icons;
    private HudOverlayPreparationContext? context;
    private RiftActivityLevel? activity;
    public SizeF Size { get; private set; }

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public RiftActivityPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null)
    {
        this.load = load ?? LoadIcons;
    }
    /// <summary>Stores the native activity category.</summary>
    public void SetContent(RiftActivityLevel value)
    {
        activity = value;
    }
    /// <summary>Loads borrowed textures once per client API and reserves a fixed 24-unit icon slot.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!activity.HasValue) return;
        if (icons == null || !ReferenceEquals(context?.Api, preparation.Api)) icons = load(preparation);
        context = preparation;
        Size = new SizeF(24, 24);
    }
    /// <summary>Draws prepared artwork without native activity sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!activity.HasValue || icons == null || context == null) return;
        double pixels = 24 * context.GuiScale;
        renderer.RenderTexture(icons[(int)activity.Value].TextureId, bounds.renderX,
            bounds.renderY + (bounds.OuterHeight - pixels) / 2, pixels, pixels, 50);
    }
    #endregion
    #region Lifetime
    /// <summary>Drops session bindings without disposing engine-owned cached textures.</summary>
    public void Reset()
    {
        icons = null;
        context = null;
        activity = null;
        Size = SizeF.Empty;
    }
    /// <summary>Releases borrowed bindings on final removal.</summary>
    public void Dispose() => Reset();
    #endregion
    #endregion
    #region Private
    /// <summary>Loads transparent rift activity assets in increasing activity order with engine cache ownership.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        string[] names = ["low", "medium", "high", "apocalyptic"];
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/rift-activity/" + names[index] + ".png"), ref texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion
}
