using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RiftActivity;

/// <summary>Borrows cached rift activity textures and draws one compact icon with a shared radial-gradient backdrop.</summary>
internal sealed class RiftActivityPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private RiftActivityLevel? activity;

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public RiftActivityPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudIndicatorTextPresentation? textStyle = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, textStyle)
    {
        this.load = load ?? LoadIcons;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Stores the native activity category.</summary>
    public void SetContent(RiftActivityLevel value)
    {
        activity = value;
    }
    /// <summary>Loads borrowed textures once per client API and reserves a fixed HudOverlaySizes.Standard-unit icon slot.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!activity.HasValue) return;
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "rift", activity.Value.ToString().ToUpperInvariant());
            return;
        }
        if (icons == null || !ReferenceEquals(Context?.Api, preparation.Api)) icons = load(preparation);
        if (backdrop == null || !ReferenceEquals(Context?.Api, preparation.Api)) backdrop = loadBackdrop(preparation);
        Context = preparation;
        Size = new SizeF(HudOverlaySizes.Standard, HudOverlaySizes.Standard);
        Visibility.Show();
    }
    /// <summary>Draws prepared artwork without native activity sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!activity.HasValue || Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (icons == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        renderer.RenderTexture(icons[(int)activity.Value].TextureId, bounds.renderX,
            bounds.renderY + (bounds.OuterHeight - pixels) / 2, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
    }
    #endregion
    /// <summary>Queues the prepared icon circle separately from foreground drawing.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!activity.HasValue || icons == null || Context == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        backdrop?.Queue(renderer, bounds.renderX, y, Context.GuiScale);
    }
    #endregion
    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        icons = null; backdrop = null;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        activity = null;
    }
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
            HudOverlayTextureFiltering.Prepare(texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion

}
