using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Composes a localized name and reading as independently owned native text elements.</summary>
internal sealed class TextView : NativeView
{
    private readonly Func<TextContent> content;
    private readonly Func<float> effect;
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.LeftMiddle);
    private readonly TextElement name, reading;

    #region Public API
    #region Composition and preparation
    /// <summary>Creates passive lettering whose resources belong to this visual instance alone.</summary>
    public TextView(ICoreClientAPI api, Func<TextContent> content, Func<float>? effect = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? create = null) : base(api)
    {
        this.content = content; this.effect = effect ?? (() => 1);
        name = Own(new TextElement(api, ElementBounds.Fixed(0, 0, 0, 0), _ => { }, create));
        try { reading = Own(new TextElement(api, ElementBounds.Fixed(0, 0, 0, 0), _ => { }, create)); }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates glyph content and native row geometry before layout.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed();
        var next = content();
        name.Prepare(context, next.Name); reading.Prepare(context, next.Reading);
        bool hasReading = !string.IsNullOrEmpty(next.Reading);
        double row = hasReading ? next.Size.Height / 2 : next.Size.Height;
        double readingHeight = hasReading ? row : 0;
        double readingY = hasReading ? row : 0;
        bool changed = root.fixedWidth != next.Size.Width || root.fixedHeight != next.Size.Height
            || name.Bounds.fixedHeight != row || reading.Bounds.fixedY != readingY || reading.Bounds.fixedHeight != readingHeight;
        root.fixedWidth = next.Size.Width; root.fixedHeight = next.Size.Height;
        name.Bounds.fixedWidth = reading.Bounds.fixedWidth = next.Size.Width;
        name.Bounds.fixedHeight = row; reading.Bounds.fixedHeight = readingHeight;
        reading.Bounds.fixedY = readingY;
        if (changed) root.MarkDirtyRecursive();
    }
    /// <summary>Caches both fitted targets after native bounds resolution.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        name.LayoutCompleted(clip); reading.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering state
    /// <summary>Combines the shared feature effect with this visual's host opacity.</summary>
    public override void SetOpacity(float opacity)
    {
        float alpha = opacity * effect();
        name.State.SetOpacity(alpha); reading.State.SetOpacity(alpha);
    }
    /// <summary>Text-only indicators have no artwork backdrop.</summary>
    public override void CollectBackdrops(IRenderAPI renderer) { }
    /// <summary>Routes failures of either text component through the feature boundary.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        name.State.Failure = reading.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers both real elements with the existing native composer.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.AddInteractiveElement(name, prefix + ":name");
            composer.AddInteractiveElement(reading, prefix + ":reading");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion
}
