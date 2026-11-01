using System;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementStyledDropDown : GuiElementDropDown
{
    private LoadedTexture idleFrame;
    private LoadedTexture hoverFrame;
    private LoadedTexture valueTexture;
    private LoadedTexture arrowTexture;
    private (double Width, double Height, double InnerWidth, double InnerHeight, double Scale, bool Enabled, string Label)? cached;
    private bool disposed;

    internal GuiElementStyledDropDown(ICoreClientAPI api, string[] values, string[] names, int selectedIndex,
        SelectionChangedDelegate changed, ElementBounds bounds, CairoFont? font = null)
        : base(api, values, names, selectedIndex, changed, ValidateBounds(values, names, selectedIndex, bounds),
            (font ?? CairoFont.WhiteSmallText().WithFontSize(GuiControlMetrics.CompactFontSize))
                .Clone().WithColor(GuiControlPalette.Color(GuiControlPalette.DropdownText)), false)
    {
        ArgumentNullException.ThrowIfNull(changed);
        idleFrame = new(api);
        hoverFrame = new(api);
        valueTexture = new(api);
        arrowTexture = new(api);
        listMenu.Dispose();
        listMenu = new GuiElementStyledListMenu(api, values, names, selectedIndex, (value, selected) =>
        {
            onSelectionChanged(value, selected);
            SetSelectedIndex(listMenu.SelectedIndex);
        }, bounds.ForkChildOffseted(-bounds.fixedX, -bounds.fixedY).WithAlignment(EnumDialogArea.None), Font);
    }

    internal static void ValidateList(string[] values, string[] names)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(names);
        if (values.Length == 0 || values.Length != names.Length || values.Any(string.IsNullOrEmpty)
            || names.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Dropdowns require matching, nonempty values and labels.");
    }

    private static ElementBounds ValidateBounds(string[] values, string[] names, int selected, ElementBounds bounds)
    {
        ValidateList(values, names);
        if (selected < -1 || selected >= values.Length) throw new ArgumentOutOfRangeException(nameof(selected));
        return bounds.WithFixedHeight(GuiControlMetrics.NativeDropdownHeight);
    }

    internal static double MeasureWidth(string[] names, CairoFont font)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0) throw new ArgumentException("Dropdowns require labels.", nameof(names));
        return names.Max(name => GuiControlMetrics.MeasureTextWidth(font, name, GuiControlMetrics.DropdownTextInset))
            + GuiControlMetrics.NativeArrowWidth;
    }

    public override void BeforeCalcBounds()
    {
        Bounds.fixedWidth = Math.Max(Bounds.fixedWidth, MeasureWidth(listMenu.Names, Font));
        base.BeforeCalcBounds();
    }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            listMenu.Enabled = value;
            if (!value) listMenu.OnFocusLost();
        }
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        using (var scratch = new ImageSurface(Format.Argb32, surface.Width, surface.Height))
        using (var native = new Context(scratch))
            base.ComposeElements(native, scratch);
        cached = null;
        EnsureTextures();
    }

    private void EnsureTextures()
    {
        ValidateList(listMenu.Values, listMenu.Names);
        int selected = listMenu.SelectedIndex;
        if (selected < 0 || selected >= listMenu.Names.Length)
            throw new InvalidOperationException("Dropdown selection is outside its current list.");
        string label = listMenu.SelectedIndices.Length == 0 ? "" : listMenu.Names[selected];
        var key = (Bounds.OuterWidth, Bounds.OuterHeight, Bounds.InnerWidth, Bounds.InnerHeight, Scale * scaled(1), Enabled, label);
        if (cached == key) return;
        if (cached is { } previous && (previous.InnerWidth != Bounds.InnerWidth
            || previous.InnerHeight != Bounds.InnerHeight || previous.Scale != Scale * scaled(1)))
            base.SetSelectedIndex(listMenu.SelectedIndices.Length == 0 ? -1 : selected);
        int width = Math.Max(1, (int)Math.Round(Bounds.OuterWidth, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(Bounds.OuterHeight, MidpointRounding.AwayFromZero));
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.DropdownFill,
            GuiControlPalette.DropdownOutline, scaled(1), ref idleFrame);
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.HoverFill,
            GuiControlPalette.SelectedOutline, scaled(1), ref hoverFrame);
        Font.Color = GuiControlPalette.Color(GuiControlPalette.DropdownText,
            Enabled ? 1 : GuiControlPalette.DisabledOpacity);
        int valueWidth = (int)((Bounds.InnerWidth - scaled(GuiControlMetrics.NativeArrowWidth)) * Scale);
        int valueHeight = (int)(scaled(GuiControlMetrics.NativeDropdownHeight) * Scale);
        if (valueWidth <= 0 || valueHeight <= 0)
            throw new InvalidOperationException("Dropdown bounds do not contain its value and arrow.");
        using (var image = new ImageSurface(Format.Argb32, valueWidth, valueHeight))
        using (var context = new Context(image))
        {
            Font.SetupContext(context);
            textUtil.DrawTextLine(context, Font, label, scaled(GuiControlMetrics.DropdownTextInset),
                (valueHeight - Font.GetFontExtents().Height) / 2);
            generateTexture(image, ref valueTexture, linearMag: false);
        }
        int arrowWidth = (int)(scaled(GuiControlMetrics.NativeArrowWidth) * Scale);
        using (var image = new ImageSurface(Format.Argb32, arrowWidth, (int)Bounds.InnerHeight))
        using (var context = new Context(image))
        {
            context.SetSourceRGBA(Font.Color);
            context.LineWidth = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.Outline, scaled(1)));
            double half = scaled(GuiControlMetrics.ChevronSize) / 2;
            double x = arrowWidth / 2d;
            double y = image.Height / 2d;
            context.MoveTo(x - half, y - half / 2);
            context.LineTo(x, y + half / 2);
            context.LineTo(x + half, y - half / 2);
            context.Stroke();
            generateTexture(image, ref arrowTexture, linearMag: false);
        }
        cached = key;
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        EnsureTextures();
        bool emphasized = Enabled && (listMenu.IsOpened || Bounds.PointInside(api.Input.MouseX, api.Input.MouseY));
        api.Render.Render2DTexturePremultipliedAlpha((emphasized ? hoverFrame : idleFrame).TextureId, Bounds);
        var nativeValue = currentValueTexture;
        var nativeReleased = arrowDownButtonReleased;
        var nativePressed = arrowDownButtonPressed;
        currentValueTexture = valueTexture;
        arrowDownButtonReleased = arrowDownButtonPressed = arrowTexture;
        try { base.RenderInteractiveElements(deltaTime); }
        finally
        {
            currentValueTexture = nativeValue;
            arrowDownButtonReleased = nativeReleased;
            arrowDownButtonPressed = nativePressed;
        }
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (Enabled) base.OnKeyDown(api, args);
    }

    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    {
        if (Enabled) base.OnKeyPress(api, args);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        idleFrame.Dispose();
        hoverFrame.Dispose();
        valueTexture.Dispose();
        arrowTexture.Dispose();
        richTextElem.Dispose();
        base.Dispose();
    }
}
