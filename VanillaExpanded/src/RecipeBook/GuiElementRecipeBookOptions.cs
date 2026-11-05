using System;
using System.Collections.Generic;
using System.Globalization;
using Cairo;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using static VanillaExpanded.RecipeBook.RecipeBookVisualMetrics;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiElementRecipeBookOptions : GuiElement
{
    internal const double IconSize = OptionsIconSize;
    private readonly IconRendererDelegate icon;
    private readonly GuiElementStyledCheckbox checkbox;
    private readonly GuiElementRecipeBookSizeInput sizeInput;
    private readonly ElementBounds popupBounds;
    private readonly GuiElementHoverText hint;
    private GuiComposer? popup;
    private LoadedTexture iconTexture;
    private LoadedTexture hoverTexture;
    private int iconSize;
    private bool mousePressed;
    private bool synchronizing;
    private bool disposing;
    internal GuiElementSwitch Checkbox => checkbox;
    internal GuiElementNumberInput SizeInput => sizeInput;
    internal bool RejectedSizeEdit => sizeInput.RejectedEdit;
    internal ElementBounds PopupBounds => popupBounds;
    internal bool IsOpened { get; private set; }
    internal void CloseMenu()
    {
        mousePressed = false;
        IsOpened = false;
        popup?.UnfocusOwnElements();
    }
    public override bool Focusable => true;
    public override double DrawOrder => 1.1;

    internal GuiElementRecipeBookOptions(ICoreClientAPI api, ElementBounds bounds) : base(api, bounds)
    {
        icon = api.Gui.Icons.SvgIconSource(api.Assets.Get(
            new AssetLocation("vanillaexpanded:textures/gui/recipe-book-options.svg")));
        var font = PopupFont();
        string label = Lang.Get("vanillaexpanded:recipebook-remember-open");
        double width = MeasurePopupWidth(font, label);
        popupBounds = ElementBounds.Fixed(bounds.fixedWidth - width, bounds.fixedHeight, width, 2 * PopupHeight).WithParent(bounds);
        bounds.WithChild(popupBounds);
        checkbox = new GuiElementStyledCheckbox(api, CommitPreference,
            ElementBounds.Fixed(PopupPadding, (PopupHeight - GuiControlMetrics.CheckboxSize) / 2,
                GuiControlMetrics.CheckboxSize, GuiControlMetrics.CheckboxSize).WithParent(popupBounds), PopupDepth + 1);
        hint = new GuiElementHoverText(api, Lang.Get("vanillaexpanded:recipebook-remember-open-hint"),
            font, HintWidth, ElementBounds.Fixed(bounds.fixedWidth - width, bounds.fixedHeight, width, PopupHeight).WithParent(bounds));
        sizeInput = new(api, ElementBounds.Fixed(width - PopupPadding - SizeInputWidth,
            PopupHeight + PopupPadding, SizeInputWidth, PopupHeight - 2 * PopupPadding).WithParent(popupBounds),
            CommitTileSize, NormalizeTileSize, font.Clone());
        SynchronizeSize();
        iconTexture = new LoadedTexture(api);
        hoverTexture = new LoadedTexture(api);
    }

    private const double SizeInputWidth = 65;

    internal static double MeasurePopupWidth(CairoFont font, string label) =>
        Math.Max(GuiControlMetrics.MeasureTextWidth(font, label, PopupPadding)
            + GuiControlMetrics.CheckboxSize + LabelSpacing,
            GuiControlMetrics.MeasureTextWidth(font, Lang.Get("vanillaexpanded:recipebook-tile-size"), PopupPadding)
                + SizeInputWidth + LabelSpacing);

    private static CairoFont PopupFont() => CairoFont.WhiteSmallText().WithFontSize(GuiControlMetrics.TextFontSize)
        .WithColor(GuiControlPalette.Color(GuiControlPalette.Text));

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        int size = iconSize = System.Math.Max(1, (int)System.Math.Round(scaled(IconSize), System.MidpointRounding.AwayFromZero));
        using var iconSurface = new ImageSurface(Format.Argb32, size, size);
        using var iconContext = genContext(iconSurface);
        icon(iconContext, 0, 0, size, size, GuiControlPalette.Color(GuiControlPalette.Text));
        generateTexture(iconSurface, ref iconTexture, linearMag: false);
        using var hoverSurface = new ImageSurface(Format.Argb32, size, size);
        using var hoverContext = genContext(hoverSurface);
        icon(hoverContext, 0, 0, size, size, GuiControlPalette.Color(GuiControlPalette.HoverIcon));
        generateTexture(hoverSurface, ref hoverTexture, linearMag: false);
        if (popup == null)
        {
            var font = PopupFont();
            double textHeight = font.GetFontExtents().Height / scaled(1);
            popup = api.Gui.CreateCompo("vanillaexpanded-recipebook-options", popupBounds);
            popup.Tabbable = false;
            popup.zDepth = PopupDepth;
            popup.AddStaticElement(new GuiElementCustomDraw(api, ElementBounds.Fill, DrawBackground))
                .AddInteractiveElement(checkbox, "remember-open")
                .AddStaticText(Lang.Get("vanillaexpanded:recipebook-remember-open"), font,
                    ElementBounds.Fixed(PopupPadding + GuiControlMetrics.CheckboxSize + LabelSpacing,
                        (PopupHeight - textHeight) / 2,
                        popupBounds.fixedWidth - 2 * PopupPadding - GuiControlMetrics.CheckboxSize - LabelSpacing, textHeight))
                .AddStaticText(Lang.Get("vanillaexpanded:recipebook-tile-size"), font.Clone(),
                    ElementBounds.Fixed(PopupPadding, PopupHeight + (PopupHeight - textHeight) / 2,
                        popupBounds.fixedWidth - 2 * PopupPadding - SizeInputWidth - LabelSpacing, textHeight))
                .AddInteractiveElement(sizeInput, "tile-size")
                .AddHoverText(Lang.Get("vanillaexpanded:recipebook-tile-size-hint", MinimumTileSize, MaximumTileSize),
                    font.Clone(), HintWidth, sizeInput.Bounds.FlatCopy())
                .Compose(false);
        }
        else popup.ReCompose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        bool hovered = IsOpened || IsPositionInside(api.Input.MouseX, api.Input.MouseY);
        api.Render.Render2DTexturePremultipliedAlpha(hovered ? hoverTexture.TextureId : iconTexture.TextureId,
            System.Math.Round(Bounds.absX, System.MidpointRounding.AwayFromZero),
            System.Math.Round(Bounds.absY, System.MidpointRounding.AwayFromZero), iconSize, iconSize, 200);
        if (IsOpened)
        {
            popup?.Render(deltaTime);
            hint.RenderInteractiveElements(deltaTime);
        }
    }

    private void Toggle()
    {
        if (IsOpened) CloseMenu();
        else
        {
            RestoreMenu(false);
        }
        api.Gui.PlaySound("menubutton");
    }

    private void CommitPreference(bool enabled)
    {
        api.Settings.Bool[RecipeBookSystem.RememberOpenSetting] = enabled;
        popup?.ReCompose();
    }

    internal void RestoreMenu(bool focusSize, string? text = null, bool rejectedEdit = false)
    {
        checkbox.SetValue(api.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
        SynchronizeSize(text);
        sizeInput.RejectedEdit = rejectedEdit;
        IsOpened = true;
        popup?.FocusElement(focusSize ? sizeInput.TabIndex : checkbox.TabIndex);
    }

    private void SynchronizeSize(string? text = null)
    {
        synchronizing = true;
        try { sizeInput.SetValue(text ?? RecipeBookSystem.GetTileSize(api).ToString(CultureInfo.InvariantCulture)); }
        finally { synchronizing = false; }
    }

    private void CommitTileSize(string text)
    {
        if (synchronizing || disposing) return;
        if (int.TryParse(text, NumberStyles.Integer, GlobalConstants.DefaultCultureInfo, out int value)
            && value is >= MinimumTileSize and <= MaximumTileSize)
            api.Settings.Int[RecipeBookSystem.TileSizeSetting] = value;
    }

    private void NormalizeTileSize()
    {
        if (synchronizing || disposing) return;
        if (sizeInput.RejectedEdit
            || !int.TryParse(sizeInput.GetText(), NumberStyles.Integer, GlobalConstants.DefaultCultureInfo, out int value)
            || value is < MinimumTileSize or > MaximumTileSize)
        {
            api.TriggerIngameError(this, "recipebook-tile-size",
                Lang.Get("vanillaexpanded:recipebook-tile-size-invalid", MinimumTileSize, MaximumTileSize));
            SynchronizeSize();
        }
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Handled) return;
        if (IsPositionInside(args.X, args.Y) && args.Button == EnumMouseButton.Left)
        {
            mousePressed = true;
            args.Handled = true;
        }
        else if (IsOpened && PopupBounds.PointInside(args.X, args.Y))
        {
            if (args.Button == EnumMouseButton.Left) popup?.OnMouseDown(args);
            args.Handled = true;
        }
        else if (IsOpened) CloseMenu();
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (IsOpened) popup?.OnMouseUp(args);
        if (args.Button != EnumMouseButton.Left || !mousePressed) return;
        mousePressed = false;
        if (IsPositionInside(args.X, args.Y)) Toggle();
        args.Handled = true;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (IsOpened && PopupBounds.PointInside(args.X, args.Y))
        {
            popup?.OnMouseMove(args);
            args.Handled = true;
        }
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!IsOpened || !PopupBounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;
        sizeInput.OnMouseWheel(api, args);
        args.SetHandled(true);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus || args.Handled) return;
        if (args.KeyCode == (int)GlKeys.Escape && IsOpened)
        {
            CloseMenu();
            args.Handled = true;
        }
        else if (IsOpened && args.KeyCode == (int)GlKeys.Tab)
        {
            if (args.ShiftPressed ? checkbox.HasFocus : sizeInput.HasFocus) CloseMenu();
            else
            {
                popup?.FocusElement(args.ShiftPressed ? checkbox.TabIndex : sizeInput.TabIndex);
                args.Handled = true;
            }
        }
        else if (IsOpened && sizeInput.HasFocus
            && args.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter)
        {
            NormalizeTileSize();
            args.Handled = true;
        }
        else if (IsOpened)
        {
            if (args.KeyCode == (int)GlKeys.KeypadEnter && checkbox.HasFocus)
            {
                var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
                checkbox.OnKeyDown(api, enter);
            }
            else popup?.OnKeyDown(args, true);
            args.Handled = true;
        }
        else if (args.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Space)
        {
            Toggle();
            args.Handled = true;
        }
    }

    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus || !IsOpened || args.Handled) return;
        popup?.OnKeyPress(args);
        args.Handled = true;
    }

    public override void OnKeyUp(ICoreClientAPI api, KeyEvent args)
    {
        if (HasFocus && IsOpened) popup?.OnKeyUp(args);
    }

    public override void OnFocusLost()
    {
        CloseMenu();
        base.OnFocusLost();
    }

    public override void Dispose()
    {
        disposing = true;
        OnFocusLost();
        if (popup != null) popup.Dispose();
        else
        {
            checkbox.Dispose();
            sizeInput.Dispose();
        }
        hint.Dispose();
        iconTexture.Dispose();
        hoverTexture.Dispose();
        base.Dispose();
    }

    internal sealed class GuiElementRecipeBookSizeInput : GuiElementNumberInput
    {
        private readonly Action lostFocus;
        internal bool RejectedEdit { get; set; }

        internal GuiElementRecipeBookSizeInput(ICoreClientAPI api, ElementBounds bounds,
            Action<string> changed, Action lostFocus, CairoFont font) : base(api, bounds, changed, font)
        {
            this.lostFocus = lostFocus;
            IntMode = true;
        }

        public override void LoadValue(List<string> newLines)
        {
            RejectedEdit = newLines.Exists(line => line is not ("" or "-")
                && !int.TryParse(line, NumberStyles.Integer, GlobalConstants.DefaultCultureInfo, out _));
            base.LoadValue(newLines);
        }

        public override void OnFocusLost()
        {
            base.OnFocusLost();
            lostFocus();
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            api.Render.GlPushMatrix();
            try
            {
                api.Render.GlTranslate(0, 0, PopupDepth);
                base.RenderInteractiveElements(deltaTime);
            }
            finally { api.Render.GlPopMatrix(); }
        }
    }

    private static void DrawBackground(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        GuiControlDrawing.Frame(ctx, bounds.drawX, bounds.drawY, bounds.InnerWidth, bounds.InnerHeight,
            GuiControlPalette.DropdownFill, GuiControlPalette.DropdownOutline, scaled(1));
    }
}
