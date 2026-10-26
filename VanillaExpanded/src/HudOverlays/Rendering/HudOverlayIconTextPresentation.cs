using System;
using System.Drawing;
using System.Linq;
using Cairo;
using VanillaExpanded.ItemRendering;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Layout;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns cloned item content, cached localized contrast text and an optional backdrop, borrowing engine icon resources.</summary>
public sealed class HudOverlayIconTextPresentation : IDisposable
{
    private readonly System.Func<HudOverlayPreparationContext, string, LoadedTexture> createText;
    private readonly System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createIconBackground;
    private readonly float iconSize;
    private readonly float gap;
    private readonly bool textBeforeIcon;
    private readonly bool useItemPresentation;
    private readonly ElementBounds iconClip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private readonly ItemSlot slot = new DummySlot();
    private byte[]? iconContent;
    private string key = string.Empty;
    private object[] arguments = Array.Empty<object>();
    private LoadedTexture? text;
    private LoadedTexture? iconBackground;
    private HudOverlayPreparationContext? context;
    private bool dirty = true;
    private bool disposed;
    private double scale = 1;
    public SizeF Size { get; private set; }
    /// <summary>Selects whether the cached reading is beside or above its item icon.</summary>
    public HudOverlayDirection Direction { get; init; } = HudOverlayDirection.Horizontal;
    #region Public API
    #region Content and preparation
    /// <summary>Uses native contrast text with optional backdrop and shared asset-authored item presentation.</summary>
    public HudOverlayIconTextPresentation(System.Func<HudOverlayPreparationContext, string, LoadedTexture>? createText = null,
        float iconSize = HudOverlaySizes.Large, bool textBeforeIcon = false, float gap = 9, float? fontSize = null, bool circularIconBackground = false,
        System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createIconBackground = null, bool useItemPresentation = false)
    {
        if (!float.IsFinite(iconSize) || iconSize <= 0) throw new ArgumentOutOfRangeException(nameof(iconSize));
        if (!float.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        if (fontSize.HasValue && (!float.IsFinite(fontSize.Value) || fontSize.Value <= 0)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        this.createIconBackground = circularIconBackground ? createIconBackground ?? CreateNativeIconBackground : null;
        this.iconSize = iconSize;
        this.textBeforeIcon = textBeforeIcon;
        this.useItemPresentation = useItemPresentation;
        this.gap = gap;
        this.createText = createText ?? ((preparation, localized) => CreateNativeText(preparation, localized, fontSize));
    }
    /// <summary>Copies sampled content so engine item rendering never receives a live inventory stack.</summary>
    public void SetContent(ItemStack? icon, string localizedKey, params object[] arguments)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(localizedKey);
        ArgumentNullException.ThrowIfNull(arguments);
        byte[]? sampledIcon = icon?.ToBytes();
        if (ReferenceEquals(icon?.Collectible, slot.Itemstack?.Collectible) && key == localizedKey && this.arguments.SequenceEqual(arguments)
            && ((iconContent == null && sampledIcon == null) || (iconContent != null && sampledIcon != null && iconContent.SequenceEqual(sampledIcon)))) return;
        // Compare serialized sampled attributes rather than render-mutated presentation copies.
        iconContent = sampledIcon;
        slot.Itemstack = icon?.Clone();
        key = localizedKey;
        this.arguments = (object[])arguments.Clone();
        dirty = true;
    }
    /// <summary>Prepares text only after sampled content, locale, font revision, or GUI scale changes.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(preparation);
        if (!Enum.IsDefined(Direction)) throw new ArgumentOutOfRangeException(nameof(Direction));
        if (!dirty && context == preparation) return;
        // The backdrop depends only on scale and API ownership; count and locale changes reuse it.
        if (createIconBackground != null && (iconBackground == null || context?.GuiScale != preparation.GuiScale || !ReferenceEquals(context?.Api, preparation.Api)))
        {
            LoadedTexture replacementBackground = createIconBackground(preparation, iconSize);
            LoadedTexture? previousBackground = iconBackground;
            iconBackground = replacementBackground;
            previousBackground?.Dispose();
        }
        // Replace the owned texture only after successful generation, retaining cleanup ownership on failures.
        string localized = Lang.GetL(preparation.Locale, key, arguments);
        LoadedTexture? replacement = string.IsNullOrEmpty(localized) ? null : createText(preparation, localized);
        LoadedTexture? previous = text;
        text = replacement;
        previous?.Dispose();
        context = preparation;
        scale = preparation.GuiScale;
        float iconWidth = slot.Itemstack == null ? 0 : iconSize;
        float textWidth = text == null ? 0 : (float)(text.Width / scale);
        float textHeight = text == null ? 0 : (float)(text.Height / scale);
        float spacing = iconWidth > 0 && text != null ? gap : 0;
        Size = Direction == HudOverlayDirection.Vertical
            ? new SizeF(Math.Max(iconWidth, textWidth), iconWidth + spacing + textHeight)
            : new SizeF(iconWidth + spacing + textWidth, Math.Max(iconWidth, textHeight));
        dirty = false;
    }
    #endregion
    #region Drawing and lifetime
    /// <summary>Draws cached pixels only; the host's assigned native scissor clips oversized icons and text.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime, float? maximumTextHeight = null, float textDepth = 50)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (dirty || context == null) return;
        double x = bounds.renderX;
        double y = bounds.renderY;
        double height = bounds.OuterHeight;
        // Optional fitting keeps a cached reading inside a fixed-height presentation without regenerating its texture.
        double textFit = text == null || !maximumTextHeight.HasValue ? 1
            : Math.Min(1, maximumTextHeight.Value * scale / text.Height);
        double textWidth = (text?.Width ?? 0) * textFit;
        double textHeight = (text?.Height ?? 0) * textFit;
        // Keep the text and icon centered independently within the allocated native bounds.
        double textX = x;
        double iconX = x;
        double iconY = y + (height - iconSize * scale) / 2;
        double textY = y + (height - textHeight) / 2;
        if (Direction == HudOverlayDirection.Vertical)
        {
            // Reserve the full stacked footprint; text retains its prepared font size and both rows share a center.
            iconX += (bounds.OuterWidth - iconSize * scale) / 2;
            double top = y + (height - Size.Height * scale) / 2;
            iconY = top + (text == null ? 0 : textHeight + (slot.Itemstack == null ? 0 : gap * scale));
            textX += (bounds.OuterWidth - textWidth) / 2;
            textY = top;
        }
        else if (slot.Itemstack != null)
        {
            if (textBeforeIcon)
            {
                // Anchor the icon to the reserved right edge; changing count width only moves the text's left edge.
                iconX = x + bounds.OuterWidth - iconSize * scale;
                textX = iconX - gap * scale - textWidth;
            }
            else if (text != null) textX += (iconSize + gap) * scale;
        }
        if (text != null) renderer.Render2DTexturePremultipliedAlpha(text.TextureId, textX, textY,
            textWidth, textHeight, textDepth, HudOverlayDrawOpacity.PremultipliedTint);
        if (slot.Itemstack != null && iconBackground != null)
            renderer.Render2DTexturePremultipliedAlpha(iconBackground.TextureId, iconX, iconY,
                iconSize * scale, iconSize * scale, 49, HudOverlayDrawOpacity.PremultipliedTint);
        if (slot.Itemstack != null) DrawItem(renderer, iconX, iconY, clip, deltaTime);
    }
    /// <summary>Releases session-owned textures and sampled copies without disposing borrowed engine resources.</summary>
    public void Reset()
    {
        LoadedTexture? previous = text;
        LoadedTexture? previousBackground = iconBackground;
        text = null;
        iconBackground = null;
        slot.Itemstack = null;
        iconContent = null;
        key = string.Empty;
        arguments = Array.Empty<object>();
        context = null;
        Size = SizeF.Empty;
        dirty = true;
        try { previous?.Dispose(); }
        finally { previousBackground?.Dispose(); }
    }
    /// <summary>Finally releases owned resources exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Reset();
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Uses shared asset-authored presentation inside the icon slot, restoring the enclosing shader and scissor.</summary>
    private void DrawItem(IRenderAPI renderer, double x, double y, RectangleF clip, float deltaTime)
    {
        float pixels = (float)(iconSize * scale);
        if (!useItemPresentation)
        {
            renderer.RenderItemstackToGui(slot, x + pixels / 2, y + pixels / 2, 50, pixels, HudOverlayDrawOpacity.ItemColor,
                deltaTime, shading: true, rotate: false, showStackSize: false);
            return;
        }
        // The enlarged head uses only the icon square; the host's member clip still bounds the whole presentation.
        double left = Math.Max(x, clip.Left), top = Math.Max(y, clip.Top);
        double right = Math.Min(x + pixels, clip.Right), bottom = Math.Min(y + pixels, clip.Bottom);
        if (right <= left || bottom <= top) return;
        iconClip.absFixedX = left;
        iconClip.absFixedY = top;
        iconClip.absInnerWidth = right - left;
        iconClip.absInnerHeight = bottom - top;
        iconClip.Initialized = true;
        renderer.PushScissor(iconClip, true);
        try
        {
            // Native texture draws may leave another program active; the shared item renderer requires the GUI program.
            IShaderProgram? previous = renderer.CurrentActiveShader;
            IShaderProgram? gui = renderer.GetEngineShader(EnumShaderProgram.Gui);
            bool switchShader = gui != null && !gui.Disposed && !gui.LoadError && !ReferenceEquals(previous, gui);
            try
            {
                if (switchShader) { previous?.Stop(); gui!.Use(); }
                if (ToolHeadPresentationSystem.Renderer?.TryRender(context!.Api, slot, x + pixels / 2, y + pixels / 2, pixels, 0, 50, HudOverlayDrawOpacity.Current) != true)
                    renderer.RenderItemstackToGui(slot, x + pixels / 2, y + pixels / 2, 50, pixels, HudOverlayDrawOpacity.ItemColor,
                        deltaTime, shading: true, rotate: false, showStackSize: false);
            }
            finally
            {
                if (switchShader) { renderer.CurrentActiveShader?.Stop(); previous?.Use(); }
            }
        }
        finally { renderer.PopScissor(); }
    }
    /// <summary>Composes a translucent black circle using the native Cairo texture upload service.</summary>
    private static LoadedTexture CreateNativeIconBackground(HudOverlayPreparationContext preparation, float diameter)
    {
        int pixels = Math.Max(1, (int)Math.Ceiling(diameter * preparation.GuiScale));
        using var surface = new ImageSurface(Format.Argb32, pixels, pixels);
        using var drawing = new Context(surface);
        drawing.SetSourceRGBA(0, 0, 0, .5);
        drawing.Arc(pixels / 2d, pixels / 2d, pixels / 2d, 0, Math.PI * 2);
        drawing.Fill();
        var texture = new LoadedTexture(preparation.Api);
        try
        {
            preparation.Api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
            return HudOverlayTextureFiltering.Prepare(texture);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
    /// <summary>Creates localized white text with a dark stroke through the borrowed native GUI service.</summary>
    private static LoadedTexture CreateNativeText(HudOverlayPreparationContext preparation, string localized, float? fontSize)
    {
        var font = CairoFont.WhiteSmallText().WithStroke(new double[] { 0, 0, 0, .85 }, 1.5);
        if (fontSize.HasValue) font.WithFontSize(fontSize.Value);
        // Cairo applies the native GUI scale internally; font sizes are absolute GUI units.

        return HudOverlayTextureFiltering.Prepare(preparation.Api.Gui.TextTexture.GenTextTexture(localized, font));
    }
    #endregion
}
