using System;
using System.Drawing;
using System.Linq;
using Cairo;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns cloned item content, cached localized contrast text and an optional backdrop, borrowing engine icon resources.</summary>
internal sealed class HudOverlayIconTextPresentation : IDisposable
{
    private readonly System.Func<HudOverlayPreparationContext, string, LoadedTexture> createText;
    private readonly System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createIconBackground;
    private readonly float iconSize;
    private readonly float gap;
    private readonly bool textBeforeIcon;
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
    #region Public API
    #region Content and preparation
    /// <summary>Uses native contrast text by default, accepting an inert resource factory for focused lifecycle checks.</summary>
    public HudOverlayIconTextPresentation(System.Func<HudOverlayPreparationContext, string, LoadedTexture>? createText = null,
        float iconSize = 32, bool textBeforeIcon = false, float gap = 6, float? fontSize = null, bool circularIconBackground = false,
        System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createIconBackground = null)
    {
        if (!float.IsFinite(iconSize) || iconSize <= 0) throw new ArgumentOutOfRangeException(nameof(iconSize));
        if (!float.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        if (fontSize.HasValue && (!float.IsFinite(fontSize.Value) || fontSize.Value <= 0)) throw new ArgumentOutOfRangeException(nameof(fontSize));
        this.createIconBackground = circularIconBackground ? createIconBackground ?? CreateNativeIconBackground : null;
        this.iconSize = iconSize;
        this.textBeforeIcon = textBeforeIcon;
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
        Size = new SizeF(iconWidth + (text == null ? 0 : (iconWidth > 0 ? gap : 0) + (float)(text.Width / scale)),
            Math.Max(iconWidth, text == null ? 0 : (float)(text.Height / scale)));
        dirty = false;
    }
    #endregion
    #region Drawing and lifetime
    /// <summary>Draws cached pixels only; the host's assigned native scissor clips oversized icons and text.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (dirty || context == null) return;
        double x = bounds.renderX;
        double y = bounds.renderY;
        double height = bounds.OuterHeight;
        // Keep the text and icon centered independently within the allocated native bounds.
        double textX = x;
        double iconX = x;
        if (text != null && slot.Itemstack != null)
        {
            if (textBeforeIcon) iconX += text.Width + gap * scale;
            else textX += (iconSize + gap) * scale;
        }
        if (text != null) renderer.Render2DTexturePremultipliedAlpha(text.TextureId, textX, y + (height - text.Height) / 2,
            text.Width, text.Height, 50);
        if (slot.Itemstack != null && iconBackground != null)
            renderer.Render2DTexturePremultipliedAlpha(iconBackground.TextureId, iconX, y + (height - iconSize * scale) / 2,
                iconSize * scale, iconSize * scale, 49);
        if (slot.Itemstack != null)
            renderer.RenderItemstackToGui(slot, iconX + iconSize * scale / 2, y + height / 2, 50, (float)(iconSize * scale), -1,
                deltaTime, shading: true, rotate: false, showStackSize: false);
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
            return texture;
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
        return preparation.Api.Gui.TextTexture.GenTextTexture(localized, font);
    }
    #endregion
}
