using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Registers independent weather channels as ordered native texture elements.</summary>
internal sealed class WeatherView : NativeView
{
    private readonly WeatherConditionsPresentation content;
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.LeftMiddle);
    private readonly TextureElement[] layers = new TextureElement[4];
    private float opacity;
    private double x, y, scale;

    #region Public API
    #region Composition and preparation
    /// <summary>Creates distinct native layer components without loading textures or sampling weather.</summary>
    public WeatherView(ICoreClientAPI api, WeatherConditionsPresentation content,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null) : base(api)
    {
        this.content = content;
        try
        {
            for (int index = 0; index < layers.Length; index++)
                layers[index] = Own(new TextureElement(api, ElementBounds.Fixed(0, 0, 0, 0), _ => { }, load));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates selected sprites and local bounds only when their prepared identities or geometry change.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed(); scale = context.GuiScale;
        bool changed = root.fixedWidth != content.Size.Width || root.fixedHeight != content.Size.Height;
        if (changed) { root.fixedWidth = content.Size.Width; root.fixedHeight = content.Size.Height; }
        for (int index = 0; index < layers.Length; index++)
        {
            var next = index < content.Layers.Length ? content.Layers[index] : null;
            layers[index].Prepare(context, next?.Path);
            var rectangle = next?.Bounds ?? RectangleF.Empty;
            var bounds = layers[index].Bounds;
            if (bounds.fixedX != rectangle.X || bounds.fixedY != rectangle.Y
                || bounds.fixedWidth != rectangle.Width || bounds.fixedHeight != rectangle.Height)
            {
                changed = true;
                bounds.fixedX = rectangle.X; bounds.fixedY = rectangle.Y;
                bounds.fixedWidth = rectangle.Width; bounds.fixedHeight = rectangle.Height;
            }
        }
        if (changed) root.MarkDirtyRecursive();
    }
    /// <summary>Caches resolved foreground targets and the single shared circle anchor.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        ThrowIfDisposed();
        foreach (var layer in layers) layer.LayoutCompleted(clip);
        x = root.renderX; y = root.renderY;
    }
    #endregion
    #region Rendering state
    /// <summary>Applies identical host opacity to every channel.</summary>
    public override void SetOpacity(float value)
    {
        ThrowIfDisposed(); opacity = value; foreach (var layer in layers) layer.State.SetOpacity(value);
    }
    /// <summary>Submits one prepared backdrop independently of the number of active weather channels.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        if (opacity <= 0 || content.Size.IsEmpty) return;
        using var scope = new DrawOpacity(opacity); content.Backdrop?.Queue(renderer, x, y, scale);
    }
    /// <summary>Isolates failures in each layer through the feature boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        foreach (var layer in layers) layer.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Mounts the four channel slots in stable back-to-front order under one native origin.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try { for (int index = 0; index < layers.Length; index++) composer.AddInteractiveElement(layers[index], prefix + ":layer-" + index); }
        finally { composer.EndChildElements(); }
    }
    #endregion
}
