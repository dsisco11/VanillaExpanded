using System;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementStyledListMenu : GuiElementListMenu
{
    private LoadedTexture styledList;
    private LoadedTexture transparentHover;
    private string[]? cachedNames;
    private (double Width, double Height, double Scale, int Hover, int Selected, bool HasSelection, bool Enabled) cached;
    private bool disposed;

    internal GuiElementStyledListMenu(ICoreClientAPI api, string[] values, string[] names, int selectedIndex,
        SelectionChangedDelegate changed, ElementBounds bounds, CairoFont font)
        : base(api, values, names, selectedIndex, changed, bounds, font.Clone(), false)
    {
        styledList = new(api);
        transparentHover = new(api);
    }

    private void EnsureTextures()
    {
        GuiElementStyledDropDown.ValidateList(Values, Names);
        var key = (expandedBoxWidth, expandedBoxHeight, Scale * scaled(1), HoveredIndex, SelectedIndex,
            SelectedIndices.Length > 0, Enabled);
        if (cachedNames != null && cached == key && Names.SequenceEqual(cachedNames, StringComparer.Ordinal)) return;
        using var image = new ImageSurface(Format.Argb32, (int)expandedBoxWidth, (int)expandedBoxHeight);
        using var ctx = new Context(image);
        double rowHeight = scaled(GuiControlMetrics.NativePopupRowHeight) * Scale;
        int stroke = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.Outline, scaled(1)));
        GuiControlDrawing.Frame(ctx, 0, 0, image.Width, image.Height,
            GuiControlPalette.DropdownFill, GuiControlPalette.DropdownOutline, scaled(1));
        if (Enabled && HoveredIndex >= 0 && HoveredIndex < Names.Length)
        {
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.HoverFill));
            ctx.Rectangle(stroke, HoveredIndex * rowHeight + stroke,
                image.Width - 2 * stroke, rowHeight - 2 * stroke);
            ctx.Fill();
        }
        if (SelectedIndices.Length > 0)
        {
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.SelectedIcon,
                Enabled ? 1 : GuiControlPalette.DisabledOpacity));
            double top = Math.Round(SelectedIndex * rowHeight, MidpointRounding.AwayFromZero) + stroke;
            double bottom = Math.Round((SelectedIndex + 1) * rowHeight, MidpointRounding.AwayFromZero) - stroke;
            ctx.Rectangle(stroke, top, Math.Max(1,
                GuiControlDrawing.Pixels(GuiControlMetrics.PopupSelectionWidth, scaled(1))), bottom - top);
            ctx.Fill();
        }
        var font = Font.Clone().WithColor(GuiControlPalette.Color(GuiControlPalette.DropdownText,
            Enabled ? 1 : GuiControlPalette.DisabledOpacity));
        for (int i = 0; i < richtTextElem.Length; i++)
        {
            var text = richtTextElem[i];
            text.SetNewTextWithoutRecompose(Names[i], font);
            text.BeforeCalcBounds();
            text.Bounds.CalcWorldBounds();
            text.ComposeFor(text.Bounds, ctx, image);
        }
        generateTexture(image, ref styledList, linearMag: false);
        if (transparentHover.TextureId == 0)
        {
            using var empty = new ImageSurface(Format.Argb32, 1, 1);
            generateTexture(empty, ref transparentHover, linearMag: false);
        }
        cachedNames = Names.ToArray();
        cached = key;
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (Enabled) base.OnKeyDown(api, args);
    }

    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    {
        if (Enabled) base.OnKeyPress(api, args);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!IsOpened) return;
        EnsureTextures();
        var nativeList = dropDownTexture;
        var nativeHover = hoverTexture;
        int depth = api.Render.ScissorStack.Count;
        dropDownTexture = styledList;
        hoverTexture = transparentHover;
        try { base.RenderInteractiveElements(deltaTime); }
        finally
        {
            dropDownTexture = nativeList;
            hoverTexture = nativeHover;
            while (api.Render.ScissorStack.Count > depth) api.Render.PopScissor();
        }
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        styledList.Dispose();
        transparentHover.Dispose();
        foreach (var text in richtTextElem) text.Dispose();
        base.Dispose();
    }
}
