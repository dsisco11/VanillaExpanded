using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiElementRecipeBookTiles : GuiElementContainer
{
    internal const int TileSize = 48;
    private readonly RecipeBookSession session;
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

    internal GuiElementRecipeBookTiles(ICoreClientAPI api, ElementBounds bounds, ElementBounds viewport,
        RecipeBookSession session, Action<CraftingRecipeSelection?> inspect, Action<int, bool> activate,
        RecipeBookIconCache cache,
        Func<IRecipeBookIconShaderProgram>? createShader = null)
        : base(api, bounds)
    {
        this.session = session;
        this.viewport = viewport;
        this.inspect = inspect;
        icons = new(api, cache, createShader);
        Tabbable = true;
        unscaledCellSpacing = 0;
        int rows = (int)Math.Ceiling(viewport.fixedHeight / TileSize) + 1;
        for (int i = 0; i < rows * Columns; i++)
        {
            var tile = new GuiElementRecipeBookTile(api,
                ElementBounds.Fixed(i % Columns * TileSize, i / Columns * TileSize, TileSize - 2, TileSize - 2),
                activate, Focused, ShowInspection);
            Add(tile);
        }
        InsideClipBounds = viewport;
        foreach (var tile in Elements) tile.InsideClipBounds = viewport;
        session.Changed += Changed;
        BindRows(false);
    }

    private int Columns => Math.Max(1, (int)(viewport.fixedWidth / TileSize));
    private double ContentHeight => Math.Max(viewport.InnerHeight,
        Math.Ceiling((double)session.Rows.Length / Columns) * scaled(TileSize));
    public override bool Focusable => !session.Rows.IsEmpty;
    internal float ScrollPosition => scroll / (float)scaled(1);

    internal void Bind(GuiElementScrollbar value) { scrollbar = value; Changed(); }

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
        firstIndex = (int)(scroll / scaled(TileSize)) * Columns;
        Bounds.fixedY = -(scroll % scaled(TileSize)) / scaled(1);
        for (int i = 0; i < Elements.Count; i++)
        {
            var tile = (GuiElementRecipeBookTile)Elements[i];
            int index = firstIndex + i;
            tile.Selection = index < session.Rows.Length ? session.Rows[index] : null;
            tile.SetActive(tile.Selection?.Output.Id == session.SelectedOutput);
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
        api.Render.PushScissor(viewport, true);
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
        base.OnMouseDown(api, args);
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        UpdateHover(args.X, args.Y);
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (viewport.PointInside(api.Input.MouseX, api.Input.MouseY)) scrollbar?.OnMouseWheel(api, args);
    }

    public override void OnFocusGained()
    {
        if (session.Rows.IsEmpty) return;
        hoverPosition = null;
        base.OnFocusGained();
        if (entryIndex is int index) Navigate(Math.Min(index, session.Rows.Length - 1));
        entryIndex = null;
        ShowInspection();
    }

    public override void OnFocusLost()
    {
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
        double top = index / Columns * scaled(TileSize);
        if (top < scroll) scroll = (float)top;
        if (top + scaled(TileSize) > scroll + viewport.InnerHeight)
            scroll = (float)(top + scaled(TileSize) - viewport.InnerHeight);
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
