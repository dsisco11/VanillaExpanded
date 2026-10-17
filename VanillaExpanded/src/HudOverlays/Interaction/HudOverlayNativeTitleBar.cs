using System;
using HarmonyLib;
using System.Drawing;
using Vintagestory.API.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Cairo;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Uses compact orange titlebar artwork with the actual native Fixed/Movable dropdown and chrome behavior.</summary>
internal sealed class HudOverlayNativeTitleBar : GuiElementDialogTitleBar
{
    // Native titlebars do not expose their menu or initial-render persistence flag. Keep version-sensitive
    // access here, without patching the engine or copying its graphics and dropdown implementation.
    private static readonly AccessTools.FieldRef<GuiElementDialogTitleBar, GuiElementListMenu> MenuField =
        AccessTools.FieldRefAccess<GuiElementDialogTitleBar, GuiElementListMenu>("listMenu");
    private static readonly AccessTools.FieldRef<GuiElementDialogTitleBar, bool> FirstRenderField =
        AccessTools.FieldRefAccess<GuiElementDialogTitleBar, bool>("firstFrameRendered");
    private static readonly AccessTools.FieldRef<GuiElementDialogTitleBar, Rectangled> MenuRectField =
        AccessTools.FieldRefAccess<GuiElementDialogTitleBar, Rectangled>("menuIconRect");
    private static readonly AccessTools.FieldRef<GuiElementDialogTitleBar, Rectangled> CloseRectField =
        AccessTools.FieldRefAccess<GuiElementDialogTitleBar, Rectangled>("closeIconRect");
    private static readonly AccessTools.FieldRef<GuiElementListMenu, double> MenuWidthField =
        AccessTools.FieldRefAccess<GuiElementListMenu, double>("expandedBoxWidth");
    private static readonly AccessTools.FieldRef<GuiElementListMenu, double> MenuHeightField =
        AccessTools.FieldRefAccess<GuiElementListMenu, double>("expandedBoxHeight");

    public GuiElementListMenu Menu => MenuField(this);
    private static readonly AccessTools.FieldRef<GuiElementDialogTitleBar, bool> MovableField =
        AccessTools.FieldRefAccess<GuiElementDialogTitleBar, bool>("movable");
    private static readonly AccessTools.FieldRef<GuiElementListMenu, SelectionChangedDelegate> SelectionField =
        AccessTools.FieldRefAccess<GuiElementListMenu, SelectionChangedDelegate>("onSelectionChanged");

    #region Public API
    #region Permission and initialization
    /// <summary>Reuses native dropdown interaction but replaces its destructive position-reset callback.</summary>
    public void ConfigurePermission(Action<bool> change, Action reset)
    {
        Menu.Values = new[] { "auto", "manual", "reset-layout" };
        Menu.Names = new[] { Lang.Get("Fixed"), Lang.Get("Movable"), Lang.Get("vanillaexpanded:hud-reset-layout") };
        SelectionField(Menu) = (code, selected) =>
        {
            if (!selected) return;
            if (code == "reset-layout") reset();
            else change(code == "manual");
        };
    }

    /// <summary>Reflects authoritative permission without invoking the native reset or drag implementation.</summary>
    public void SynchronizePermission(bool movable)
    {
        MovableField(this) = movable;
        if (!Menu.IsOpened) Menu.SelectedIndex = movable ? 1 : 0;
    }
    /// <summary>Initializes native titlebar resources; the container supplies its caption and owns content persistence.</summary>
    public HudOverlayNativeTitleBar(ICoreClientAPI api, GuiComposer composer, Action dismiss, ElementBounds bounds)
        : base(api, string.Empty, composer, dismiss, font: CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text), bounds: bounds)
    {
        // The native first render saves its parent origin, which here is a transient hover-control origin.
        // Suppress that one write rather than allowing it to overwrite the saved content position.
        FirstRenderField(this) = true;
    }

    #endregion
    #region Composition and pointer geometry
    /// <summary>Initializes native resources, then paints a slim captioned panel with aligned menu and close glyphs.</summary>
    public override void ComposeTextElements(Context context, ImageSurface surface)
    {
        // Native composition owns restoration, dropdown preparation and hover textures. Replace only the static
        // titlebar paint and register hit rectangles matching the compact geometry; never mutate global GUI style.
        base.ComposeTextElements(context, surface);
        context.Save();
        context.Operator = Operator.Source;
        var orange = RadialMenuWedgeStyle.HoverFill;
        context.SetSourceRGBA(orange.X, orange.Y, orange.Z, .92);
        context.Rectangle(Bounds.bgDrawX, Bounds.bgDrawY, Bounds.OuterWidth, Bounds.OuterHeight);
        context.Fill();
        context.Operator = Operator.Over;
        double closeSize = scaled(unscaledCloseIconSize), menuSize = scaled(unscaledCloseIconSize + 2);
        double closeX = Bounds.OuterWidth - closeSize - scaled(8);
        double menuX = closeX - menuSize - scaled(8);
        // Keep long localized group names inside the drag area without painting over native menu/close controls.
        context.Save();
        context.Rectangle(Bounds.bgDrawX, Bounds.bgDrawY, Math.Max(0, menuX - scaled(6)), Bounds.InnerHeight);
        context.Clip();
        Font.SetupContext(context);
        DrawTextLineAt(context, text, scaled(6), (Bounds.InnerHeight - Font.GetFontExtents().Height) / 2);
        context.Restore();
        double closeY = (Bounds.InnerHeight - closeSize) / 2;
        double menuY = (Bounds.InnerHeight - menuSize) / 2;
        CloseRectField(this) = new Rectangled(closeX, closeY, closeSize, closeSize);
        MenuRectField(this) = new Rectangled(menuX, menuY, menuSize, menuSize);
        context.SetSourceRGBA(GuiStyle.DialogDefaultTextColor);
        api.Gui.Icons.DrawCross(context, Bounds.drawX + closeX, Bounds.drawY + closeY, scaled(2), closeSize);
        api.Gui.Icons.Drawmenuicon_svg(context, (int)(Bounds.drawX + menuX), (int)(Bounds.drawY + menuY),
            (int)menuSize, (int)menuSize, GuiStyle.DialogDefaultTextColor);
        context.Restore();
        Menu.Bounds.fixedX = menuX / RuntimeEnv.GUIScale;
        Menu.Bounds.MarkDirtyRecursive();
        Menu.ComposeDynamicElements();
    }

    /// <summary>Identifies the native menu/close icons so their presses do not begin a content drag.</summary>
    public bool IsChrome(int x, int y) => MenuRectField(this).PointInside(x - Bounds.absX, y - Bounds.absY)
        || CloseRectField(this).PointInside(x - Bounds.absX, y - Bounds.absY);

    /// <summary>Keeps the actual native dropdown reachable at screen edges without changing its artwork or selection rules.</summary>
    public void FitMenu(RectangleF viewport)
    {
        if (!Menu.IsOpened) return;
        double x = Math.Clamp(Menu.Bounds.renderX, viewport.Left,
            Math.Max(viewport.Left, viewport.Right - MenuWidthField(Menu)));
        double y = Math.Clamp(Menu.Bounds.renderY, viewport.Top,
            Math.Max(viewport.Top, viewport.Bottom - Menu.Bounds.InnerHeight - MenuHeightField(Menu)));
        if (x == Menu.Bounds.renderX && y == Menu.Bounds.renderY) return;
        Menu.Bounds.fixedX += (x - Menu.Bounds.renderX) / RuntimeEnv.GUIScale;
        Menu.Bounds.fixedY += (y - Menu.Bounds.renderY) / RuntimeEnv.GUIScale;
        Menu.Bounds.MarkDirtyRecursive();
        Menu.Bounds.CalcWorldBounds();
        // Recompute the native scissor and scrollbar after moving the dropdown anchor.
        Menu.ComposeDynamicElements();
    }
    #endregion
    #endregion
}
