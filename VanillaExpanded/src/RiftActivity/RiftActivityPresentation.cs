using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RiftActivity;

/// <summary>Borrows cached rift textures and owns a localized activity abbreviation below the icon.</summary>
internal sealed class RiftActivityPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private RiftActivityLevel? activity;
    private readonly HudOverlayIconTextPresentation label;
    private readonly ElementBounds labelBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public RiftActivityPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudIndicatorTextPresentation? textStyle = null, HudOverlayIconTextPresentation? label = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, textStyle)
    {
        this.load = load ?? LoadIcons;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
        this.label = label ?? new HudOverlayIconTextPresentation(fontSize: HudOverlaySizes.Text);
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
        label.SetContent(null, "vanillaexpanded:rift-activity-short-" + activity.Value.ToString().ToLowerInvariant());
        label.Prepare(preparation);
        Size = new SizeF(Math.Max(HudOverlaySizes.Standard, label.Size.Width),
            HudOverlaySizes.Standard + HudOverlaySizes.ReadingGap + label.Size.Height);
        Visibility.Show();
    }
    /// <summary>Draws prepared artwork without native activity sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!activity.HasValue || Context == null) return;
        if (TryDrawText(renderer, bounds)) return;
        if (icons == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        double x = bounds.renderX + (bounds.OuterWidth - pixels) / 2;
        double y = bounds.renderY + (bounds.OuterHeight - Size.Height * Context.GuiScale) / 2;
        renderer.RenderTexture(icons[(int)activity.Value].TextureId, x, y, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
        // Keep the cached reading centered below the artwork without changing its font size to fit the icon.
        labelBounds.absFixedX = bounds.renderX + (bounds.OuterWidth - label.Size.Width * Context.GuiScale) / 2;
        labelBounds.absFixedY = y + pixels + HudOverlaySizes.ReadingGap * Context.GuiScale;
        labelBounds.absInnerWidth = label.Size.Width * Context.GuiScale;
        labelBounds.absInnerHeight = label.Size.Height * Context.GuiScale;
        labelBounds.Initialized = true;
        label.Draw(renderer, labelBounds, new RectangleF((float)bounds.renderX, (float)bounds.renderY,
            (float)bounds.OuterWidth, (float)bounds.OuterHeight), 0);
    }
    #endregion
    /// <summary>Queues the prepared icon circle separately from foreground drawing.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!activity.HasValue || icons == null || Context == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - Size.Height * Context.GuiScale) / 2;
        backdrop?.Queue(renderer, bounds.renderX + (bounds.OuterWidth - pixels) / 2, y, Context.GuiScale);
    }
    /// <summary>Releases the owned label and shared presentation state.</summary>
    public override void Dispose()
    {
        base.Dispose();
        label.Dispose();
    }
    #endregion
    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        icons = null; backdrop = null; label.Reset();
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
