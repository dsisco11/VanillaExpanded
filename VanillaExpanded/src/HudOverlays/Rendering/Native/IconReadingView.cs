using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Composes a native reading above native artwork with independent element resources.</summary>
internal sealed class IconReadingView : NativeView
{
    private readonly Func<IconReadingContent> content;
    private readonly ElementBounds root = ElementBounds.Fixed(0, 0, 0, 0).WithAlignment(EnumDialogArea.CenterMiddle);
    private readonly TextElement reading;
    private readonly TextureElement icon;
    private readonly Func<float> effect;
    private readonly Func<IHudOverlayIconBackdrop?>? backdrop;
    private double scale;
    private float hostOpacity = 1;

    #region Public API
    /// <summary>Gets the prepared intrinsic footprint, excluding absent rows and their spacing.</summary>
    public SizeF Size { get; private set; }
    #region Preparation
    /// <summary>Creates distinct passive components borrowing stable content from one controller.</summary>
    public IconReadingView(ICoreClientAPI api, Func<IconReadingContent> content,
        Action<Exception> failure,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? loadIcon = null,
        Func<IHudOverlayIconBackdrop?>? backdrop = null, Func<float>? effect = null, double strokeWidth = 1) : base(api)
    {
        ArgumentNullException.ThrowIfNull(content);
        this.content = content;
        this.backdrop = backdrop; this.effect = effect ?? (() => 1);
        reading = Own(new TextElement(api, ElementBounds.Fixed(0, 0, 0, 0)
            .WithAlignment(EnumDialogArea.CenterTop), failure, createText, strokeWidth));
        try
        {
            icon = Own(new TextureElement(api, ElementBounds.Fixed(0, 0, 0, 0)
                .WithAlignment(EnumDialogArea.CenterTop), failure, loadIcon));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Updates changed resources and declares native row sizes without resolving bounds.</summary>
    public override void Prepare(HudOverlayPreparationContext context)
    {
        ThrowIfDisposed();
        var next = content();
        Validate(next);
        reading.Prepare(context, next.Reading);
        icon.Prepare(context, next.IconPath);
        scale = context.GuiScale * next.IconSize.Width / HudOverlaySizes.Standard;
        var textSize = next.ReadingSize ?? reading.Size;
        float readingFit = textSize.Height > 0 ? Math.Min(1, next.MaximumReadingHeight / textSize.Height) : 1;
        textSize = new(textSize.Width * readingFit, textSize.Height * readingFit);
        SizeF iconSize = string.IsNullOrEmpty(next.IconPath) ? SizeF.Empty : next.IconSize;
        float gap = textSize.Height > 0 && iconSize.Height > 0 ? next.Gap : 0;
        SizeF nextSize = new(Math.Max(textSize.Width, iconSize.Width),
            Math.Max(next.MinimumHeight, textSize.Height + gap + iconSize.Height));
        // Fixed dimensions and native alignment express the arrangement; the host calculates them later.
        bool changed = SetBounds(root, nextSize, 0);
        changed |= SetBounds(reading.Bounds, textSize, 0);
        changed |= SetBounds(icon.Bounds, iconSize, textSize.Height + gap);
        Size = nextSize;
        if (changed) root.MarkDirtyRecursive();
    }
    /// <summary>Finalizes fit and clipping after the owning host calculates or composes native bounds.</summary>
    public override void LayoutCompleted(RectangleF clip)
    {
        ThrowIfDisposed();
        reading.LayoutCompleted(clip);
        icon.LayoutCompleted(clip);
    }
    #endregion
    #region Rendering state
    /// <summary>Updates both components using the same prepared host multiplier.</summary>
    public override void SetOpacity(float opacity)
    {
        ThrowIfDisposed();
        hostOpacity = opacity;
        reading.State.SetOpacity(opacity * effect()); icon.State.SetOpacity(opacity * effect());
    }
    /// <summary>Submits the cached icon target; feature pulse affects foreground only.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        if (!icon.State.Drawable) return;
        using var opacity = new DrawOpacity(hostOpacity);
        backdrop?.Invoke()?.Queue(renderer, icon.State.Target.X, icon.State.Target.Y, scale);
    }
    /// <summary>Assigns the same feature failure boundary to each independently drawn component.</summary>
    public override void SetFailureHandler(Action<Exception> failure)
    {
        reading.State.Failure = failure; icon.State.Failure = failure;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers actual child elements, rather than a full-presentation drawing callback.</summary>
    protected override void AddElements(GuiComposer composer, string prefix)
    {
        composer.BeginChildElements(root);
        try
        {
            composer.AddInteractiveElement(reading, prefix + ":reading");
            composer.AddInteractiveElement(icon, prefix + ":icon");
        }
        finally { composer.EndChildElements(); }
    }
    #endregion

    #region Private
    /// <summary>Updates native fields only when caller-supplied component geometry changes.</summary>
    private static bool SetBounds(ElementBounds bounds, SizeF size, float y)
    {
        if (bounds.fixedWidth == size.Width && bounds.fixedHeight == size.Height && bounds.fixedY == y) return false;
        bounds.fixedWidth = size.Width; bounds.fixedHeight = size.Height; bounds.fixedY = y;
        return true;
    }
    /// <summary>Checks sampled geometry before modifying native elements or owned resources.</summary>
    private static void Validate(IconReadingContent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!float.IsFinite(value.IconSize.Width) || value.IconSize.Width < 0
            || !float.IsFinite(value.IconSize.Height) || value.IconSize.Height < 0
            || !float.IsFinite(value.Gap) || value.Gap < 0
            || !float.IsFinite(value.MinimumHeight) || value.MinimumHeight < 0
            || float.IsNaN(value.MaximumReadingHeight) || value.MaximumReadingHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
    #endregion
}
