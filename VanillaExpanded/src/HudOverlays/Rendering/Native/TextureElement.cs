using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using AssetLocation = Vintagestory.API.Common.AssetLocation;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Renders prepared texture artwork and owns only explicitly generated textures.</summary>
internal sealed class TextureElement : GuiElement
{
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding> load;
    private readonly Action draw;
    private TextureBinding? binding;
    private string? path;
    private bool disposed;
    private readonly bool guiTexture;

    #region Public API
    /// <summary>Gets cached clipping, geometry and opacity.</summary>
    public ElementState State { get; }
    #region Preparation
    /// <summary>Creates a passive component with explicit asset ownership and failure isolation.</summary>
    public TextureElement(ICoreClientAPI api, ElementBounds bounds, Action<Exception> failure,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null, bool guiTexture = false) : base(api, bounds)
    {
        this.load = load ?? LoadTexture;
        this.guiTexture = guiTexture;
        State = new(failure); draw = DrawTexture;
    }
    /// <summary>Loads only changed artwork; scale and movement reuse the prepared asset.</summary>
    public void Prepare(HudOverlayPreparationContext context, string? assetPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(api, context.Api)) throw new ArgumentException("A native element cannot change API ownership.", nameof(context));
        if (path == assetPath) return;
        var replacement = string.IsNullOrEmpty(assetPath) ? (TextureBinding?)null : load(context, assetPath);
        var previous = binding;
        binding = replacement; path = assetPath;
        if (previous is { Owned: true } old && !ReferenceEquals(old.Texture, replacement?.Texture)) old.Texture.Dispose();
    }
    /// <summary>Snapshots resolved native artwork bounds before drawing or backdrop collection.</summary>
    public void LayoutCompleted(RectangleF clip)
    {
        State.SetGeometry(new((float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight), clip);
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Draws cached artwork without any resource generation or component layout.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!disposed && binding != null) State.Render(api.Render, draw);
    }
    /// <summary>Leaves mouse presses untouched for the separate input host.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases generated artwork once and drops borrowed engine bindings.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var previous = binding; binding = null; path = null;
        if (previous is { Owned: true } owned) owned.Texture.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Submits the prepared artwork rectangle using straight-alpha opacity.</summary>
    private void DrawTexture()
    {
        var target = State.Target;
        if (guiTexture) api.Render.Render2DTexture(binding!.Value.Texture.TextureId, target.X, target.Y, target.Width, target.Height, 50, State.Tint(false));
        else api.Render.RenderTexture(binding!.Value.Texture.TextureId, target.X, target.Y, target.Width, target.Height, 50, State.Tint(false));
    }
    /// <summary>Rasterizes owned SVGs or borrows engine-cached PNGs with the existing sampling policy.</summary>
    private static TextureBinding LoadTexture(HudOverlayPreparationContext context, string path)
    {
        var location = new AssetLocation(Constants.ModId, path);
        if (path.EndsWith(".svg", StringComparison.Ordinal))
        {
            var owned = context.Api.Gui.LoadSvg(location, 64, 64, 64, 64);
            try { return new(HudOverlayTextureFiltering.Prepare(owned), true); }
            catch { owned.Dispose(); throw; }
        }
        var texture = new LoadedTexture(context.Api);
        context.Api.Render.GetOrLoadTexture(location, ref texture);
        texture.IgnoreUndisposed = true;
        return new(HudOverlayTextureFiltering.Prepare(texture), false);
    }
    #endregion
}
