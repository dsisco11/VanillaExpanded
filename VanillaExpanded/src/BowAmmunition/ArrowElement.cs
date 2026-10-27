using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Owns one render-mutated arrow copy and its circular background within a native icon slot.</summary>
internal sealed class ArrowElement : GuiElement
{
    private readonly ItemSlot slot = new DummySlot();
    private readonly System.Func<HudOverlayPreparationContext, float, LoadedTexture> createBackground;
    private readonly Action draw;
    private ItemStack? sampled;
    private LoadedTexture? background;
    private double scale;
    private bool disposed;
    private float deltaTime;
    private int color;

    #region Public API
    /// <summary>Gets cached geometry and the shared feature failure boundary.</summary>
    public ElementState State { get; } = new(_ => { });
    #region Preparation
    /// <summary>Creates passive arrow artwork with a replaceable background upload boundary.</summary>
    public ArrowElement(ICoreClientAPI api, ElementBounds bounds,
        System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createBackground = null) : base(api, bounds)
    {
        this.createBackground = createBackground ?? CreateBackground; draw = DrawArrow;
    }
    /// <summary>Clones a new stable sample and prepares only changed background resources.</summary>
    public void Prepare(HudOverlayPreparationContext context, ItemStack? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(value, sampled)) { slot.Itemstack = value?.Clone(); sampled = value; }
        if (value == null) { background?.Dispose(); background = null; }
        if (value != null && (background == null || scale != context.GuiScale))
        {
            var replacement = createBackground(context, HudOverlaySizes.Small);
            background?.Dispose(); background = replacement;
        }
        scale = context.GuiScale;
    }
    /// <summary>Caches the icon-specific scissor before any item rendering.</summary>
    public void LayoutCompleted(RectangleF clip)
    {
        var target = new RectangleF((float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.OuterWidth, (float)Bounds.OuterHeight);
        var intersection = RectangleF.Intersect(target, clip);
        if (intersection.Width <= 0 || intersection.Height <= 0) intersection = RectangleF.Empty;
        State.SetGeometry(target, intersection);
    }
    #endregion
    #region Rendering and lifetime
    /// <summary>Caches packed item tint alongside the ordinary texture multiplier.</summary>
    public void SetOpacity(float opacity)
    {
        State.SetOpacity(opacity); color = unchecked(((int)Math.Round(opacity * 255) << 24) | 0xFFFFFF);
    }
    /// <summary>Draws the prepared arrow inside cached bounds without modifying component layout.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (disposed || slot.Itemstack == null) return;
        this.deltaTime = deltaTime; State.Render(api.Render, draw);
    }
    /// <summary>Leaves passive arrow presses to the HUD's input owner.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    /// <summary>Releases owned background and copied content once despite duplicate composer disposal.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; background?.Dispose(); background = null; sampled = null; slot.Itemstack = null;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Preserves the enclosing shader while using asset-authored item presentation and native fallback.</summary>
    private void DrawArrow()
    {
        var target = State.Target;
        if (background != null) api.Render.Render2DTexturePremultipliedAlpha(background.TextureId,
            (double)target.X, target.Y, target.Width, target.Height, 49, State.Tint(true));
        var renderer = api.Render;
        IShaderProgram? previous = renderer.CurrentActiveShader;
        IShaderProgram? gui = renderer.GetEngineShader(EnumShaderProgram.Gui);
        bool switchShader = gui != null && !gui.Disposed && !gui.LoadError && !ReferenceEquals(previous, gui);
        try
        {
            if (switchShader) { previous?.Stop(); gui!.Use(); }
            if (ToolHeadPresentationSystem.Renderer?.TryRender(api, slot, target.X + target.Width / 2,
                target.Y + target.Height / 2, target.Width, 0, 50, State.Opacity) != true)
                renderer.RenderItemstackToGui(slot, target.X + target.Width / 2, target.Y + target.Height / 2,
                    50, target.Width, color, deltaTime, shading: true, rotate: false, showStackSize: false);
        }
        finally { if (switchShader) { renderer.CurrentActiveShader?.Stop(); previous?.Use(); } }
    }
    /// <summary>Uploads the same translucent circle used by the arrow's compact presentation.</summary>
    private static LoadedTexture CreateBackground(HudOverlayPreparationContext context, float diameter)
    {
        int pixels = Math.Max(1, (int)Math.Ceiling(diameter * context.GuiScale));
        using var surface = new ImageSurface(Format.Argb32, pixels, pixels);
        using var drawing = new Context(surface);
        drawing.SetSourceRGBA(0, 0, 0, .5);
        drawing.Arc(pixels / 2d, pixels / 2d, pixels / 2d, 0, Math.PI * 2); drawing.Fill();
        var texture = new LoadedTexture(context.Api);
        try
        {
            context.Api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
            return HudOverlayTextureFiltering.Prepare(texture);
        }
        catch { texture.Dispose(); throw; }
    }
    #endregion
}
