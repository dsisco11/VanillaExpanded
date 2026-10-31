using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Hosts a native single-selection list without titlebar reflection or engine patches.</summary>
internal sealed class HudOverlayListMenu : GuiElementListMenu, IHudMenuPopup
{
    private bool disposed;
    #region Public API
    #region Preparation and geometry
    /// <summary>Creates the native list; preparation remains explicit so ownership is established before graphics allocation.</summary>
    public HudOverlayListMenu(ICoreClientAPI api, string[] codes, string[] names, int selected,
        SelectionChangedDelegate select)
        : base(api, codes, names, selected, select, ElementBounds.Fixed(0, 0, 1, 26).WithParent(api.Gui.WindowBounds),
            CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.MenuText), false)
    {
        unscaledLineHeight = Bounds.fixedHeight;
        // The default callback rounds offsets to whole rows, leaving the final row partially hidden in a
        // short viewport. Retain the native scrollbar, but use its continuous offset for complete access.
        scrollbar.Dispose();
        scrollbar = new HudMenuScrollbar(api, value => scrollOffY = value, () => visibleBounds);
    }

    /// <summary>Gets protected native visible measurements rather than the inaccurate anchor hit rectangle.</summary>
    public RectangleF VisibleBounds => visibleBounds == null ? RectangleF.Empty
        : new((float)visibleBounds.renderX, (float)visibleBounds.renderY,
            (float)visibleBounds.OuterWidth, (float)visibleBounds.OuterHeight);

    /// <summary>Prepares once, positions the actual list, and retains native scrolling for clipped height.</summary>
    public bool Prepare(PointF pointer, RectangleF viewport)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        double scale = RuntimeEnv.GUIScale;
        if (!double.IsFinite(scale) || scale <= 0 || viewport.Width < 2 || viewport.Height < 2 || Values.Length == 0) return false;
        // Measure at the screen origin first: native lists render below a nonvisible anchor row.
        Bounds.fixedX = 0;
        Bounds.fixedY = -Bounds.fixedHeight;
        MaxHeight = Math.Max(1, (int)Math.Ceiling(viewport.Height / scale));
        Open();
        RectangleF fitted = HudPopupPlacement.Fit(pointer, new((float)expandedBoxWidth, (float)expandedBoxHeight), viewport);
        if (fitted.IsEmpty) { OnFocusLost(); return false; }
        Bounds.fixedX = fitted.X / scale;
        Bounds.fixedY = fitted.Y / scale - Bounds.fixedHeight;
        Bounds.MarkDirtyRecursive();
        Bounds.CalcWorldBounds();
        // Native rendering scales the prepared surface to expandedBoxWidth. Constrain both the surface and
        // scrollbar geometry together when a localized label is wider than the viewport.
        expandedBoxWidth = fitted.Width;
        visibleBounds = ElementBounds.Fixed(fitted.X / scale, fitted.Y / scale, fitted.Width / scale,
            fitted.Height / scale).WithParent(api.Gui.WindowBounds);
        visibleBounds.CalcWorldBounds();
        scrollbar.Bounds.WithFixedPosition(Math.Max(0, fitted.Width / scale - 10), 0)
            .WithFixedSize(Math.Min(10, fitted.Width / scale), Math.Max(1, fitted.Height / scale - 3));
        scrollbar.Bounds.MarkDirtyRecursive();
        scrollbar.SetHeights((int)fitted.Height, (int)expandedBoxHeight);
        ComposeFittedScrollbar(fitted);
        if (SelectedIndex >= 0 && SelectedIndex < Values.Length) EnsureItemVisible(SelectedIndex);
        OnFocusGained();
        return true;
    }

    /// <summary>Restricts hit-testing to the visible popup instead of its native anchor row and offscreen rows.</summary>
    public override bool IsPositionInside(int x, int y) => !disposed && IsOpened && VisibleBounds.Contains(x, y);
    #endregion
    #region Rendering and pointer delegation
    /// <inheritdoc />
    public void Draw(float deltaTime) { if (!disposed) RenderInteractiveElements(deltaTime); }
    /// <inheritdoc />
    public void MouseDown(MouseEvent args) { if (!disposed) OnMouseDown(api, args); }
    /// <inheritdoc />
    public void MouseUp(MouseEvent args) { if (!disposed) OnMouseUp(api, args); }
    /// <inheritdoc />
    public void MouseMove(MouseEvent args) { if (!disposed) OnMouseMove(api, args); }
    /// <inheritdoc />
    public void MouseWheel(MouseWheelEventArgs args) { if (!disposed) OnMouseWheel(api, args); }
    #endregion
    #region Keyboard and disposal
    /// <inheritdoc />
    public void KeyDown(KeyEvent args) { if (!disposed) OnKeyDown(api, args); }
    /// <inheritdoc />
    public void KeyPress(KeyEvent args) { if (!disposed) OnKeyPress(api, args); }
    /// <summary>Releases native resources and the protected rich-text children omitted by native list disposal.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        OnFocusLost();
        try { foreach (var text in richtTextElem) text.Dispose(); }
        finally { base.Dispose(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Rebuilds the native track surface after fitting; the engine draws this surface outside the list's content scissor.</summary>
    private void ComposeFittedScrollbar(RectangleF fitted)
    {
        // Track artwork is a full-width transparent surface with the track at its right edge. Changing only
        // expandedBoxWidth leaves that artwork at the original position, so compose against fitted extents.
        scrollbar.Bounds.WithEmptyParent();
        scrollbar.Bounds.CalcWorldBounds();
        using var surface = new ImageSurface(Format.Argb32, Math.Max(1, (int)fitted.Width), Math.Max(1, (int)fitted.Height));
        using var context = genContext(surface);
        scrollbar.ComposeElements(context, surface);
        generateTexture(surface, ref scrollbarTexture);
    }
    #endregion
}
