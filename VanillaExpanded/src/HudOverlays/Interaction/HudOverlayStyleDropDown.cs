using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Uses native dropdown chrome with the bounded, explicitly owned native list adapter.</summary>
internal sealed class HudOverlayStyleDropDown : GuiElementDropDown
{
    private readonly HudOverlayListMenu menu;
    private readonly RectangleF viewport;
    private bool disposed;
    #region Public API
    /// <summary>Replaces the public native list before composition, retaining native value and arrow artwork.</summary>
    public HudOverlayStyleDropDown(ICoreClientAPI api, string[] codes, string[] labels, int selected,
        SelectionChangedDelegate select, ElementBounds bounds, CairoFont font, RectangleF viewport)
        : base(api, codes, labels, selected, select, bounds, font, false)
    {
        this.viewport = viewport;
        listMenu.Dispose();
        menu = new HudOverlayListMenu(api, codes, labels, selected, (code, on) =>
        {
            SetSelectedValue(code);
            onSelectionChanged?.Invoke(code, on);
        });
        listMenu = menu;
    }
    /// <summary>Gets expanded list bounds for the parent popup's hit-testing.</summary>
    public RectangleF ExpandedBounds => menu.IsOpened ? menu.VisibleBounds : RectangleF.Empty;
    /// <summary>Opens the list below its field, fitting at every screen edge without consuming the opening press as a selection.</summary>
    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (disposed || !Enabled || args.Button != Vintagestory.API.Common.EnumMouseButton.Left) return;
        if (menu.IsOpened) { menu.MouseDown(args); return; }
        if (!Bounds.PointInside(args.X, args.Y)) return;
        menu.Prepare(new PointF((float)Bounds.renderX, (float)(Bounds.renderY + Bounds.OuterHeight)), viewport);
        args.Handled = true;
    }
    /// <summary>Releases the native current-value rich text omitted by native dropdown disposal.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { richTextElem.Dispose(); }
        finally { base.Dispose(); }
    }
    #endregion
}
