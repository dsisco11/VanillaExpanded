using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Composes a percentage row above two filled, outlined native drop sprites.</summary>
internal sealed class WetnessView : NativeView
{
    private readonly PlayerWetnessPresentation content;
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterMiddle);
    private readonly ElementBounds artwork = ElementBounds.Fixed(0, HudOverlaySizes.WetnessReadingHeight,
        HudOverlaySizes.Small, HudOverlaySizes.Small).WithAlignment(EnumDialogArea.CenterTop);
    private readonly TextElement reading;
    private readonly TextureElement[] drops = new TextureElement[4];
    private float opacity;
    private double x, y, scale;

    #region Public API
    #region Preparation and composition
    /// <summary>Creates independently owned passive components without sampling or binding feature events.</summary>
    public WetnessView(ICoreClientAPI api, PlayerWetnessPresentation content,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null) : base(api)
    {
        this.content = content;
        reading = Own(new TextElement(api, ElementBounds.Fixed(0, 0, 0, HudOverlaySizes.WetnessReadingHeight)
            .WithAlignment(EnumDialogArea.CenterTop), _ => { }, createText, 1.5));
        try
        {
            for (int index = 0; index < drops.Length; index++)
            {
                // Each authored drop has independent fill/outline elements sharing the same local rectangle.
                bool first = index < 2;
                var bounds = first ? ElementBounds.Fixed(1.5, 5, 15, 15) : ElementBounds.Fixed(11.5, 4, 9, 9);
                drops[index] = Own(new TextureElement(api, bounds, _ => { }, load, guiTexture: true));
                if (index % 2 == 0) drops[index].State.SetColor(.25f, .65f, 1);
            }
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates dirty glyphs and establishes local native bounds before host layout.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed(); scale = context.GuiScale;
        reading.Prepare(context, content.Reading);
        for (int index = 0; index < drops.Length; index++)
            drops[index].Prepare(context, "textures/hud/player-wetness/" + (index % 2 == 0 ? "fill-mask" : "outline") + ".png");
        bool changed = root.fixedWidth != content.Size.Width || root.fixedHeight != content.Size.Height
            || reading.Bounds.fixedWidth != content.ReadingWidth;
        if (changed)
        {
            root.fixedWidth = content.Size.Width; root.fixedHeight = content.Size.Height;
            reading.Bounds.fixedWidth = content.ReadingWidth; root.MarkDirtyRecursive();
        }
    }
    /// <summary>Caches fitted text, sprite targets and the full-slot backdrop anchor after native resolution.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        ThrowIfDisposed(); reading.LayoutCompleted(clip);
        foreach (var drop in drops) drop.LayoutCompleted(clip);
        x = artwork.renderX; y = artwork.renderY;
    }
    #endregion
    #region Rendering state
    /// <summary>Shares host visibility and drag opacity across every layer and the reading.</summary>
    public override void SetOpacity(float value)
    {
        ThrowIfDisposed(); opacity = value; reading.State.SetOpacity(value);
        foreach (var drop in drops) drop.State.SetOpacity(value);
    }
    /// <summary>Queues the cached full-slot circle without arranging any component.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        if (opacity <= 0) return;
        using var scope = new DrawOpacity(opacity); content.Backdrop?.Queue(renderer, x, y, scale * HudOverlaySizes.Small / HudOverlaySizes.Standard);
    }
    /// <summary>Connects independently rendered layers to the feature failure boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        reading.State.Failure = failure; foreach (var drop in drops) drop.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers separate native fill/outline layers and lettering in foreground order.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.BeginChildElements(artwork);
            try { for (int index = 0; index < drops.Length; index++) composer.AddInteractiveElement(drops[index], prefix + ":drop-" + index); }
            finally { composer.EndChildElements(); }
            composer.AddInteractiveElement(reading, prefix + ":reading");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion
}
