using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using AssetLocation = Vintagestory.API.Common.AssetLocation;

namespace VanillaExpanded.WindSpeed;

/// <summary>Pairs the approved double-hook wind icon with a normal HUD percentage reading.</summary>
internal sealed class WindSpeedPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private readonly Func<HudOverlayPreparationContext, LoadedTexture> load;
    private readonly HudOverlayIconTextPresentation label;
    private readonly ElementBounds labelBounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private LoadedTexture? icon;
    private int percentage;

    #region Public API
    #region Content and rendering
    /// <summary>Accepts optional SVG and text factories while retaining ownership of their prepared resources.</summary>
    public WindSpeedPresentation(Func<HudOverlayPreparationContext, LoadedTexture>? load = null,
        HudIndicatorTextPresentation? text = null, HudOverlayIconTextPresentation? label = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, text)
    {
        this.load = load ?? LoadIcon;
        this.label = label ?? new HudOverlayIconTextPresentation(fontSize: HudOverlaySizes.Text);
    }

    /// <summary>Stores the sampled whole wind percentage outside drawing.</summary>
    public void SetContent(int value) => percentage = value;

    /// <summary>Prepares artwork and normal-size text, allowing localized readings to expand the footprint.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (percentage <= 0) return;
        const string key = "vanillaexpanded:wind-speed-reading";
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "wind-speed", Lang.GetL(preparation.Locale, key, percentage));
            return;
        }
        if (icon == null || !ReferenceEquals(Context?.Api, preparation.Api))
        {
            // Replace only after a successful load, retaining cleanup ownership if rasterization fails.
            var replacement = load(preparation);
            icon?.Dispose();
            icon = replacement;
        }
        label.SetContent(null, key, percentage);
        label.Prepare(preparation);
        Context = preparation;
        Size = new SizeF(Math.Max(HudOverlaySizes.Small, label.Size.Width),
            HudOverlaySizes.Small + HudOverlaySizes.ReadingGap + label.Size.Height);
        Visibility.Show();
    }

    /// <summary>Centers the icon below its cached reading without fitting or shrinking the text.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (Context == null || Size.IsEmpty) return;
        if (TryDrawText(renderer, bounds)) return;
        if (icon == null) return;
        double scale = Context.GuiScale;
        double pixels = HudOverlaySizes.Small * scale;
        double x = bounds.renderX + (bounds.OuterWidth - pixels) / 2;
        double top = bounds.renderY + (bounds.OuterHeight - Size.Height * scale) / 2;
        double y = top + (label.Size.Height + HudOverlaySizes.ReadingGap) * scale;
        renderer.RenderTexture(icon.TextureId, x, y, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
        // Give lettering its full measured bounds so native GUI scaling never becomes text fitting.
        labelBounds.absFixedX = bounds.renderX + (bounds.OuterWidth - label.Size.Width * scale) / 2;
        labelBounds.absFixedY = top;
        labelBounds.absInnerWidth = label.Size.Width * scale;
        labelBounds.absInnerHeight = label.Size.Height * scale;
        labelBounds.Initialized = true;
        label.Draw(renderer, labelBounds, new RectangleF((float)bounds.renderX, (float)bounds.renderY,
            (float)bounds.OuterWidth, (float)bounds.OuterHeight), 0);
    }
    #endregion

    #region Lifetime
    /// <summary>Ends ownership of the wind label and shared text resources.</summary>
    public override void Dispose()
    {
        base.Dispose();
        label.Dispose();
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Releases owned SVG and label resources while retaining the sampled percentage across style changes.</summary>
    protected override void ReleaseArtwork()
    {
        icon?.Dispose();
        icon = null;
        label.Reset();
    }

    /// <summary>Clears the wind percentage after a completed exit or session reset.</summary>
    protected override void ClearContent() => percentage = 0;
    #endregion

    #region Private
    /// <summary>Rasterizes the approved SVG into an owned texture using shared HUD filtering.</summary>
    private static LoadedTexture LoadIcon(HudOverlayPreparationContext preparation)
    {
        var location = new AssetLocation(Constants.ModId, "textures/hud/wind-speed/icon.svg");
        return HudOverlayTextureFiltering.Prepare(preparation.Api.Gui.LoadSvg(location, 64, 64, 64, 64));
    }
    #endregion
}
