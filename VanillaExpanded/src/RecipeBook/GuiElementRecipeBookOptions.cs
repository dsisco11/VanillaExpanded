using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiElementRecipeBookOptions : GuiElement
{
    private readonly IconRendererDelegate icon;
    private readonly PopupCheckbox checkbox;
    private readonly ElementBounds popupBounds;
    private readonly GuiElementHoverText hint;
    private GuiComposer? popup;
    private LoadedTexture iconTexture;
    private LoadedTexture hoverTexture;
    private int iconSize;
    private bool mousePressed;
    internal GuiElementSwitch Checkbox => checkbox;
    internal ElementBounds PopupBounds => popupBounds;
    internal bool IsOpened { get; private set; }
    internal void CloseMenu()
    {
        mousePressed = false;
        IsOpened = false;
        checkbox.OnFocusLost();
    }
    public override bool Focusable => true;
    public override double DrawOrder => 1.1;

    internal GuiElementRecipeBookOptions(ICoreClientAPI api, ElementBounds bounds) : base(api, bounds)
    {
        icon = api.Gui.Icons.SvgIconSource(api.Assets.Get(
            new AssetLocation("vanillaexpanded:textures/gui/recipe-book-options.svg")));
        var font = CairoFont.WhiteSmallText();
        string label = Lang.Get("vanillaexpanded:recipebook-remember-open");
        double width = System.Math.Ceiling(font.GetTextExtents(label).Width / scaled(1) + 40);
        popupBounds = ElementBounds.Fixed(bounds.fixedWidth - width, bounds.fixedHeight, width, 30).WithParent(bounds);
        bounds.WithChild(popupBounds);
        checkbox = new PopupCheckbox(api, CommitPreference,
            ElementBounds.Fixed(5, 6, 18, 18).WithParent(popupBounds));
        hint = new GuiElementHoverText(api, Lang.Get("vanillaexpanded:recipebook-remember-open-hint"),
            font, 300, ElementBounds.Fixed(bounds.fixedWidth - width, bounds.fixedHeight, width, 30).WithParent(bounds));
        iconTexture = new LoadedTexture(api);
        hoverTexture = new LoadedTexture(api);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        int size = iconSize = System.Math.Max(1, (int)System.Math.Round(scaled(17), System.MidpointRounding.AwayFromZero));
        using var iconSurface = new ImageSurface(Format.Argb32, size, size);
        using var iconContext = genContext(iconSurface);
        icon(iconContext, 0, 0, size, size, GuiStyle.DialogDefaultTextColor);
        generateTexture(iconSurface, ref iconTexture, linearMag: false);
        using var hoverSurface = new ImageSurface(Format.Argb32, size, size);
        using var hoverContext = genContext(hoverSurface);
        icon(hoverContext, 0, 0, size, size, [0, 0.8, 0, 0.6]);
        generateTexture(hoverSurface, ref hoverTexture, linearMag: false);
        if (popup == null)
        {
            var font = CairoFont.WhiteSmallText();
            double textHeight = font.GetFontExtents().Height / scaled(1);
            popup = api.Gui.CreateCompo("vanillaexpanded-recipebook-options", popupBounds);
            popup.Tabbable = false;
            popup.zDepth = 310;
            popup.AddStaticElement(new GuiElementCustomDraw(api, ElementBounds.Fill, DrawBackground))
                .AddInteractiveElement(checkbox, "remember-open")
                .AddStaticText(Lang.Get("vanillaexpanded:recipebook-remember-open"), font,
                    ElementBounds.Fixed(28, (30 - textHeight) / 2, popupBounds.fixedWidth - 33, textHeight))
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
            checkbox.SetValue(api.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
            popup?.ReCompose();
            IsOpened = true;
            checkbox.OnFocusGained();
        }
        api.Gui.PlaySound("menubutton");
    }

    private void CommitPreference(bool enabled)
    {
        api.Settings.Bool[RecipeBookSystem.RememberOpenSetting] = enabled;
        popup?.ReCompose();
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
            if (args.Button == EnumMouseButton.Left) checkbox.OnMouseDownOnElement(api, args);
            args.Handled = true;
        }
        else if (IsOpened) CloseMenu();
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left || !mousePressed) return;
        mousePressed = false;
        if (IsPositionInside(args.X, args.Y)) Toggle();
        args.Handled = true;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (IsOpened && PopupBounds.PointInside(args.X, args.Y))
        {
            args.Handled = true;
        }
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus || args.Handled) return;
        if (args.KeyCode == (int)GlKeys.Escape && IsOpened)
        {
            CloseMenu();
            args.Handled = true;
        }
        else if (args.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Space)
        {
            if (IsOpened)
            {
                if (args.KeyCode == (int)GlKeys.KeypadEnter)
                {
                    var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
                    checkbox.OnKeyDown(api, enter);
                    args.Handled = enter.Handled;
                }
                else checkbox.OnKeyDown(api, args);
            }
            else Toggle();
            args.Handled = true;
        }
    }

    public override void OnFocusLost()
    {
        CloseMenu();
        base.OnFocusLost();
    }

    public override void Dispose()
    {
        OnFocusLost();
        if (popup != null) popup.Dispose();
        else checkbox.Dispose();
        hint.Dispose();
        iconTexture.Dispose();
        hoverTexture.Dispose();
        base.Dispose();
    }

    private static void DrawBackground(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        RoundRectangle(ctx, bounds.drawX, bounds.drawY, bounds.InnerWidth, bounds.InnerHeight, 1);
        ctx.SetSourceRGBA(GuiStyle.DialogStrongBgColor);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(0, 0, 0, 0.5);
        ctx.LineWidth = 2;
        ctx.Stroke();
    }

    private sealed class PopupCheckbox(ICoreClientAPI api, System.Action<bool> changed, ElementBounds bounds)
        : GuiElementSwitch(api, changed, bounds, 18, 2)
    {
        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            base.ComposeElements(ctx, surface);
            if (!On) return;
            double padding = scaled(2);
            RoundRectangle(ctx, Bounds.drawX + padding, Bounds.drawY + padding,
                Bounds.InnerWidth - 2 * padding, Bounds.InnerHeight - 2 * padding, 1);
            ctx.SetSourceRGBA(0, 0, 0, 1);
            ctx.FillPreserve();
            fillWithPattern(api, ctx, waterTextureName, false, true, 255, 0.5f);
        }

        // Bake the checked state into the popup texture so it shares the dropdown's foreground depth.
        public override void RenderInteractiveElements(float deltaTime) { }
    }
}
