using System;
using Cairo;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Paints a compact group caption; movement and persistence belong to the shared container controls.</summary>
internal sealed class HudOverlayGroupTitleBar : GuiElementTextBase
{
    #region Public API
    /// <summary>Creates caption artwork without native window menus, close controls, or position side effects.</summary>
    public HudOverlayGroupTitleBar(ICoreClientAPI api, ElementBounds bounds)
        : base(api, string.Empty, CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text), bounds) { }

    /// <summary>Paints the radial-menu orange panel and clips the localized caption to its drag area.</summary>
    public override void ComposeTextElements(Context context, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        context.Save();
        try
        {
            var orange = RadialMenuWedgeStyle.HoverFill;
            context.SetSourceRGBA(orange.X, orange.Y, orange.Z, .92);
            context.Rectangle(Bounds.bgDrawX, Bounds.bgDrawY, Bounds.OuterWidth, Bounds.OuterHeight);
            context.Fill();
            context.Rectangle(Bounds.bgDrawX, Bounds.bgDrawY, Bounds.OuterWidth, Bounds.OuterHeight);
            context.Clip();
            Font.SetupContext(context);
            DrawTextLineAt(context, text, scaled(6), Math.Max(0, (Bounds.InnerHeight - Font.GetFontExtents().Height) / 2));
        }
        finally { context.Restore(); }
    }
    #endregion
}
