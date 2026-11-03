using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using AssetLocation = Vintagestory.API.Common.AssetLocation;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Renders prepared texture artwork and owns only explicitly generated textures.</summary>
internal sealed class TextureElement : GuiElement
{
    private readonly TextureLoader load;
    private readonly Action draw;
    private TextureBinding? binding;
    private string? path, loadedPath;
    private HudOverlayPreparationContext? context;
    private Size loadedSize;
    private bool disposed, ready, isSvg;
    private readonly bool guiTexture, premultiplied;
    private readonly float depth;

    #region Public API
    /// <summary>Gets cached clipping, geometry and opacity.</summary>
    public ElementState State { get; }
    #region Preparation
    /// <summary>Creates a passive component with explicit asset ownership and failure isolation.</summary>
    public TextureElement(ICoreClientAPI api, ElementBounds bounds, Action<Exception> failure,
        TextureLoader? load = null, bool guiTexture = false,
        float depth = 50, bool premultiplied = false) : base(api, bounds)
    {
        this.load = load ?? LoadTexture;
        this.guiTexture = guiTexture; this.depth = depth; this.premultiplied = premultiplied;
        State = new(failure); draw = DrawTexture;
    }
    /// <summary>Stages SVG identities for resolved layout and loads changed non-SVG artwork immediately.</summary>
    public void Prepare(HudOverlayPreparationContext context, string? assetPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(api, context.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(context));
        this.context = context;
        if (path == assetPath) return;
        bool svg = IsSvg(assetPath);
        if (svg)
        {
            path = assetPath; isSvg = svg; ready = false;
            return;
        }
        var replacement = string.IsNullOrEmpty(assetPath) ? (TextureBinding?)null : load(context, assetPath, Size.Empty);
        ReplaceBinding(replacement, assetPath, Size.Empty);
        path = assetPath; isSvg = svg; ready = replacement.HasValue;
    }
    /// <summary>Rasterizes changed SVG pixel canvases and publishes their integer native draw targets before rendering.</summary>
    public void LayoutCompleted(RectangleF clip)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ready = false;
        var target = isSvg ? PixelTarget() : new RectangleF((float)Bounds.renderX, (float)Bounds.renderY,
            (float)Bounds.OuterWidth, (float)Bounds.OuterHeight);
        State.SetGeometry(target, clip);
        if (isSvg)
        {
            var size = new Size(checked((int)target.Width), checked((int)target.Height));
            if (size.Width == 0 || size.Height == 0)
            {
                ReplaceBinding(null, null, Size.Empty);
                return;
            }
            if (loadedPath != path || loadedSize != size)
            {
                var preparation = context ?? throw new InvalidOperationException("Prepare SVG content before resolving its layout.");
                var replacement = load(preparation, path!, size);
                try
                {
                    if (!replacement.Owned || replacement.Texture is not { } texture
                        || texture.Width != size.Width || texture.Height != size.Height)
                        throw new InvalidOperationException($"SVG '{path}' must produce an owned {size.Width} x {size.Height} texture.");
                    HudOverlayTextureFiltering.PrepareNativePixels(texture);
                }
                catch
                {
                    if (replacement is { Owned: true, Texture: not null }
                        && !ReferenceEquals(binding?.Texture, replacement.Texture)) replacement.Texture.Dispose();
                    throw;
                }
                ReplaceBinding(replacement, path, size);
            }
        }
        ready = binding.HasValue && loadedPath == path;
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Draws cached artwork without any resource generation or component layout.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed && ready && binding != null) State.Render(api.Render, draw);
    }
    /// <summary>Leaves mouse presses untouched for the separate input host.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases generated artwork once and drops borrowed engine bindings.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ready = false; path = null; isSvg = false; context = null;
        ReplaceBinding(null, null, Size.Empty);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Submits the prepared artwork rectangle using straight-alpha opacity.</summary>
    private void DrawTexture()
    {
        var target = State.Target;
        var texture = binding!.Value.Texture;
        float width = isSvg ? texture.Width : target.Width;
        float height = isSvg ? texture.Height : target.Height;
        if (premultiplied) api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, target.X, target.Y, width, height, depth, State.Tint(true));
        else if (guiTexture) api.Render.Render2DTexture(texture.TextureId, target.X, target.Y, width, height, depth, State.Tint(false));
        else api.Render.RenderTexture(texture.TextureId, target.X, target.Y, width, height, depth, State.Tint(false));
    }
    private static bool IsSvg(string? assetPath) => assetPath?.EndsWith(".svg", StringComparison.Ordinal) == true;

    private RectangleF PixelTarget()
    {
        double x = Bounds.renderX, y = Bounds.renderY, width = Bounds.OuterWidth, height = Bounds.OuterHeight;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException(nameof(Bounds), "SVG bounds must be finite with non-negative dimensions.");
        // Truncate shared edges, rather than sizes, so adjoining layers cannot gain a gap or overlap.
        double left = Math.Truncate(x), top = Math.Truncate(y);
        int pixelsWide = checked((int)(Math.Truncate(x + width) - left));
        int pixelsHigh = checked((int)(Math.Truncate(y + height) - top));
        return new((float)left, (float)top, pixelsWide, pixelsHigh);
    }

    private void ReplaceBinding(TextureBinding? replacement, string? identity, Size size)
    {
        var previous = binding;
        binding = replacement; loadedPath = identity; loadedSize = size;
        if (previous is { Owned: true } old && !ReferenceEquals(old.Texture, replacement?.Texture)) old.Texture.Dispose();
    }
    /// <summary>Rasterizes owned SVGs or borrows engine-cached PNGs with their existing sampling policy.</summary>
    private TextureBinding LoadTexture(HudOverlayPreparationContext context, string path, Size svgSize)
    {
        var location = new AssetLocation(Constants.ModId, path);
        if (!svgSize.IsEmpty)
        {
            var owned = context.Api.Gui.LoadSvg(location, svgSize.Width, svgSize.Height, svgSize.Width, svgSize.Height)
                ?? throw new InvalidOperationException($"Could not rasterize SVG '{location}' at {svgSize.Width} x {svgSize.Height} pixels.");
            return new(owned, true);
        }
        var texture = new LoadedTexture(context.Api);
        context.Api.Render.GetOrLoadTexture(location, ref texture);
        texture.IgnoreUndisposed = true;
        return new(HudOverlayTextureFiltering.Prepare(texture), false);
    }
    #endregion
}
