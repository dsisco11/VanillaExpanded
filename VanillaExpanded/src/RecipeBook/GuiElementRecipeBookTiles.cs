using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiElementRecipeBookTiles : GuiElementContainer
{
    internal const double TileSize = RecipeBookTileGeometry.TileSize;
    internal const double RowPitch = RecipeBookTileGeometry.Pitch;
    internal RecipeBookTileGeometry Geometry { get; }
    private readonly RecipeBookSession session;
    private readonly double compositionScale;
    private readonly ElementBounds viewport;
    private readonly Action<CraftingRecipeSelection?> inspect;
    private readonly RecipeBookIconRenderer icons;
    private GuiElementScrollbar? scrollbar;
    private (int X, int Y)? hoverPosition;
    private float scroll;
    private int firstIndex;
    private int focusedIndex;
    private int? entryIndex;
    private bool changingFocus;
    private bool updatingScrollbar;
    private bool disposed;
    private double previousHeight = -1;
    private double previousContentHeight = -1;
    private int? restoredInspection;
    private bool keyboardFocus;

    internal GuiElementRecipeBookTiles(ICoreClientAPI api, ElementBounds bounds, ElementBounds viewport,
        RecipeBookSession session, Action<CraftingRecipeSelection?> inspect, Action<int, bool> activate,
        RecipeBookIconCache cache,
        Func<IRecipeBookIconShaderProgram>? createShader = null)
        : base(api, bounds)
    {
        this.session = session;
        compositionScale = scaled(1);
        this.viewport = viewport;
        Geometry = new(viewport.fixedWidth);
        this.inspect = inspect;
        icons = new(api, cache, createShader);
        Tabbable = true;
        unscaledCellSpacing = 0;
        int rows = (int)Math.Ceiling(viewport.fixedHeight / RowPitch) + 1;
        for (int i = 0; i < rows * Columns; i++)
        {
            var tile = new GuiElementRecipeBookTile(api,
                ElementBounds.Fixed(0, 0, TileSize, TileSize),
                activate, Focused, ShowInspection);
            Add(tile);
        }
        InsideClipBounds = viewport;
        foreach (var tile in Elements) tile.InsideClipBounds = InsideClipBounds;
        session.Changed += Changed;
        BindRows(false);
    }

    private int Columns => Geometry.Columns;
    private double ContentHeight => Math.Max(viewport.InnerHeight, compositionScale * Geometry.ContentHeight(session.Rows.Length));
    public override bool Focusable => !session.Rows.IsEmpty;
    internal float ScrollPosition => scroll / (float)compositionScale;

    internal void Bind(GuiElementScrollbar value) { scrollbar = value; Changed(); }

    internal RecipeBookTileView CaptureView()
    {
        int index = Math.Min((int)(ScrollPosition / RowPitch) * Columns, Math.Max(0, session.Rows.Length - 1));
        return new(session.Rows.IsEmpty ? null : session.Rows[index].Output.Id, index, ScrollPosition % RowPitch,
            (CurrentTabIndexElement as GuiElementRecipeBookTile)?.Selection?.Output.Id,
            InspectedRow()?.Output.Id, HasFocus, keyboardFocus);
    }

    internal void RestoreView(RecipeBookTileView view)
    {
        Scroll(0);
        int Find(int? output, int fallback)
        {
            for (int i = 0; i < session.Rows.Length; i++)
                if (session.Rows[i].Output.Id == output) return i;
            return fallback;
        }
        int anchor = Find(view.AnchorOutput, Math.Min(view.AnchorIndex, Math.Max(0, session.Rows.Length - 1)));
        Scroll((float)(compositionScale * (anchor / Columns * RowPitch + view.RowOffset)));
        if (view.HasFocus && !session.Rows.IsEmpty)
        {
            // Re-enter through native focus lifecycle so its keyboard-only outline survives recomposition.
            base.OnFocusLost();
            if (view.KeyboardFocus) base.OnKeyDown(api, new KeyEvent { KeyCode = (int)GlKeys.Tab });
            changingFocus = true;
            try { OnFocusGained(); }
            finally { changingFocus = false; }
            keyboardFocus = view.KeyboardFocus;
            Navigate(Find(view.FocusedOutput, anchor));
            if (view.KeyboardFocus) base.OnKeyUp(api, new KeyEvent { KeyCode = (int)GlKeys.Tab });
        }
        restoredInspection = view.InspectedOutput;
        ShowInspection();
    }

    internal void ResetScroll()
    {
        foreach (GuiElementRecipeBookTile tile in Elements) tile.CancelPress();
        Scroll(0);
    }

    internal void Scroll(float value)
    {
        if (Elements.Cast<GuiElementRecipeBookTile>().Any(tile => tile.Pressed))
        {
            if (scrollbar != null) scrollbar.CurrentYPosition = scroll;
            return;
        }
        scroll = (float)Math.Clamp(value, 0, Math.Max(0, ContentHeight - viewport.InnerHeight));
        if (scrollbar != null) scrollbar.CurrentYPosition = scroll;
        if (updatingScrollbar) return;
        hoverPosition = null;
        restoredInspection = null;
        UnfocusOwnElements();
        session.Interact(null);
        BindRows();
        ShowInspection();
    }

    private void Changed()
    {
        if (disposed) return;
        int? focusedOutput = (CurrentTabIndexElement as GuiElementRecipeBookTile)?.Selection?.Output.Id;
        focusedIndex = Math.Clamp(focusedIndex, 0, Math.Max(0, session.Rows.Length - 1));
        if (focusedOutput != null)
            for (int i = 0; i < session.Rows.Length; i++)
                if (session.Rows[i].Output.Id == focusedOutput)
                {
                    focusedIndex = i;
                    break;
                }
        if (scrollbar != null && (previousHeight != viewport.InnerHeight || previousContentHeight != ContentHeight))
        {
            previousHeight = viewport.InnerHeight;
            previousContentHeight = ContentHeight;
            updatingScrollbar = true;
            try { scrollbar.SetHeights((float)previousHeight, (float)previousContentHeight); }
            finally { updatingScrollbar = false; }
        }
        scroll = (float)Math.Clamp(scroll, 0, Math.Max(0, ContentHeight - viewport.InnerHeight));
        if (scrollbar != null) scrollbar.CurrentYPosition = scroll;
        BindRows();
        if (session.Rows.IsEmpty && HasFocus) OnFocusLost();
        else if (HasFocus && focusedOutput != null && !Elements.Cast<GuiElementRecipeBookTile>().Any(tile => tile.Pressed))
            Navigate(focusedIndex);
        ShowInspection();
    }

    private void BindRows(bool recalculate = true)
    {
        firstIndex = (int)(scroll / (compositionScale * RowPitch)) * Columns;
        Bounds.fixedY = 0;
        for (int i = 0; i < Elements.Count; i++)
        {
            var tile = (GuiElementRecipeBookTile)Elements[i];
            int index = firstIndex + i;
            tile.Selection = index < session.Rows.Length ? session.Rows[index] : null;
            tile.Selected = tile.Selection?.Output.Id == session.SelectedOutput;
            Geometry.Place(tile.Bounds, i % Columns, i / Columns, ScrollPosition % RowPitch, compositionScale);
        }
        if (recalculate)
        {
            Bounds.MarkDirtyRecursive();
            Bounds.CalcWorldBounds();
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        icons.Prepare(Elements);
        // Native atlas baking can change the active GL scissor rectangle.
        api.Render.PushScissor(InsideClipBounds, true);
        try
        {
            base.RenderInteractiveElements(deltaTime);
            icons.Render(Elements);
        }
        finally { api.Render.PopScissor(); }
    }

    internal void UpdateHover(int x, int y)
    {
        hoverPosition = (x, y);
        restoredInspection = null;
        ShowInspection();
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        UpdateHover(args.X, args.Y);
        base.OnMouseMove(api, args);
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        entryIndex = null;
        keyboardFocus = false;
        base.OnMouseDown(api, args);
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        UpdateHover(args.X, args.Y);
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (InsideClipBounds.PointInside(api.Input.MouseX, api.Input.MouseY)) scrollbar?.OnMouseWheel(api, args);
    }

    public override void OnFocusGained()
    {
        if (session.Rows.IsEmpty) return;
        hoverPosition = null;
        restoredInspection = null;
        if (CurrentTabIndexElement == null) keyboardFocus = entryIndex != null;
        base.OnFocusGained();
        if (entryIndex is int index) Navigate(Math.Min(index, session.Rows.Length - 1));
        entryIndex = null;
        ShowInspection();
    }

    public override void OnFocusLost()
    {
        restoredInspection = null;
        keyboardFocus = false;
        base.OnFocusLost();
        ShowInspection();
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus)
        {
            entryIndex = args.KeyCode == (int)GlKeys.Tab && !session.Rows.IsEmpty
                ? args.ShiftPressed ? session.Rows.Length - 1 : 0 : null;
            base.OnKeyDown(api, args);
            return;
        }
        if (session.Rows.IsEmpty) return;
        if (Elements.Cast<GuiElementRecipeBookTile>().Any(tile => tile.Pressed)) return;
        restoredInspection = null;
        int offset = args.KeyCode switch
        {
            (int)GlKeys.Left => -1,
            (int)GlKeys.Right => 1,
            (int)GlKeys.Up => -Columns,
            (int)GlKeys.Down => Columns,
            _ => 0
        };
        if (offset != 0)
        {
            hoverPosition = null;
            Navigate(Math.Clamp(focusedIndex + offset, 0, session.Rows.Length - 1));
            args.Handled = true;
        }
        else
        {
            if (args.KeyCode is (int)GlKeys.Tab or (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Space)
                hoverPosition = null;
            if (args.KeyCode == (int)GlKeys.Tab && !args.Handled) keyboardFocus = true;
            base.OnKeyDown(api, args);
            if (args.KeyCode == (int)GlKeys.Tab && !args.Handled && !args.CtrlPressed
                && CurrentTabIndexElement != null)
            {
                int next = focusedIndex + (args.ShiftPressed ? -1 : 1);
                if (next >= 0 && next < session.Rows.Length)
                {
                    Navigate(next);
                    args.Handled = true;
                }
            }
            ShowInspection();
        }
    }

    public override void OnKeyUp(ICoreClientAPI api, KeyEvent args)
    {
        entryIndex = null;
        base.OnKeyUp(api, args);
    }

    private void Focused(GuiElementRecipeBookTile tile)
    {
        if (changingFocus || tile.Selection == null) return;
        if (tile.Pressed)
        {
            focusedIndex = firstIndex + Elements.IndexOf(tile);
            ShowInspection();
            return;
        }
        Navigate(firstIndex + Elements.IndexOf(tile));
    }

    private void Navigate(int index)
    {
        focusedIndex = index;
        double top = index / Columns * compositionScale * RowPitch;
        double bottom = top + compositionScale * RecipeBookTileGeometry.MinimumHeight;
        if (top < scroll) scroll = (float)top;
        if (bottom > scroll + viewport.InnerHeight)
            scroll = (float)(bottom - viewport.InnerHeight);
        if (scrollbar != null) scrollbar.CurrentYPosition = scroll;
        BindRows();
        changingFocus = true;
        try { FocusElement(index - firstIndex); }
        finally { changingFocus = false; }
        ShowInspection();
    }

    private void ShowInspection()
    {
        // A held press freezes ordering even when the pointer leaves its tile.
        var pressed = Elements.Cast<GuiElementRecipeBookTile>().FirstOrDefault(tile => tile.Pressed);
        session.Interact(pressed?.Selection?.Output.Id ?? InspectedRow()?.Output.Id);
        inspect(InspectedRow());
    }

    private CraftingRecipeSelection? InspectedRow()
    {
        var pressed = Elements.Cast<GuiElementRecipeBookTile>().FirstOrDefault(tile => tile.Pressed);
        var hovered = hoverPosition is { } position ? Elements.Cast<GuiElementRecipeBookTile>()
            .FirstOrDefault(tile => tile.Visible && tile.IsPositionInside(position.X, position.Y)) : null;
        var focused = CurrentTabIndexElement as GuiElementRecipeBookTile;
        if (hoverPosition == null && restoredInspection is int output)
            return session.Rows.FirstOrDefault(row => row.Output.Id == output);
        var tile = hoverPosition != null ? hovered : pressed ?? (HasFocus ? focused : null);
        return tile?.IsInViewport == true ? tile.Selection : null;
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session.Changed -= Changed;
        icons.Dispose();
        base.Dispose();
    }
}
