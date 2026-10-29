using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class GuiElementCraftingTiles : GuiElementContainer
{
    internal const int TileSize = 48;
    private readonly CraftingMenuSession session;
    private readonly ElementBounds viewport;
    private readonly Action<CraftingRecipeSelection?> inspect;
    private readonly CraftingIconRenderer icons;
    private GuiElementScrollbar? scrollbar;
    private GuiElementCraftingTile? hovered;
    private float scroll;
    private int firstIndex;
    private int focusedIndex;
    private int? entryIndex;
    private bool changingFocus;
    private bool updatingScrollbar;
    private bool disposed;
    private double previousHeight = -1;
    private double previousContentHeight = -1;

    internal GuiElementCraftingTiles(ICoreClientAPI api, ElementBounds bounds, ElementBounds viewport,
        CraftingMenuSession session, Action<CraftingRecipeSelection?> inspect, Action<int, bool> activate,
        CraftingIconCache cache,
        Func<ICraftingIconShaderProgram>? createShader = null)
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
            var tile = new GuiElementCraftingTile(api,
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

    internal void Bind(GuiElementScrollbar value) { scrollbar = value; Changed(); }

    internal void Scroll(float value)
    {
        if (Elements.Cast<GuiElementCraftingTile>().Any(tile => tile.Pressed))
        {
            if (scrollbar != null) scrollbar.CurrentYPosition = scroll;
            return;
        }
        scroll = (float)Math.Clamp(value, 0, Math.Max(0, ContentHeight - viewport.InnerHeight));
        if (updatingScrollbar) return;
        hovered = null;
        UnfocusOwnElements();
        session.Interact(null);
        BindRows();
        ShowInspection();
    }

    private void Changed()
    {
        if (disposed) return;
        focusedIndex = Math.Clamp(focusedIndex, 0, Math.Max(0, session.Rows.Length - 1));
        if (scrollbar != null && (previousHeight != viewport.InnerHeight || previousContentHeight != ContentHeight))
        {
            previousHeight = viewport.InnerHeight;
            previousContentHeight = ContentHeight;
            updatingScrollbar = true;
            try { scrollbar.SetHeights((float)previousHeight, (float)previousContentHeight); }
            finally { updatingScrollbar = false; }
        }
        BindRows();
        if (session.Rows.IsEmpty && HasFocus) OnFocusLost();
        ShowInspection();
    }

    private void BindRows(bool recalculate = true)
    {
        firstIndex = (int)(scroll / scaled(TileSize)) * Columns;
        Bounds.fixedY = -(scroll % scaled(TileSize)) / scaled(1);
        for (int i = 0; i < Elements.Count; i++)
        {
            var tile = (GuiElementCraftingTile)Elements[i];
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
        base.RenderInteractiveElements(deltaTime);
        icons.Render(Elements);
    }

    internal void UpdateHover(int x, int y)
    {
        hovered = Elements.Cast<GuiElementCraftingTile>()
            .FirstOrDefault(tile => tile.Visible && tile.IsPositionInside(x, y));
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
        hovered = null;
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
        if (Elements.Cast<GuiElementCraftingTile>().Any(tile => tile.Pressed)) return;
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
            hovered = null;
            Navigate(Math.Clamp(focusedIndex + offset, 0, session.Rows.Length - 1));
            args.Handled = true;
        }
        else
        {
            if (args.KeyCode is (int)GlKeys.Tab or (int)GlKeys.Enter or (int)GlKeys.KeypadEnter or (int)GlKeys.Space)
                hovered = null;
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

    private void Focused(GuiElementCraftingTile tile)
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
        var pressed = Elements.Cast<GuiElementCraftingTile>().FirstOrDefault(tile => tile.Pressed);
        var focused = CurrentTabIndexElement as GuiElementCraftingTile;
        var row = (pressed ?? hovered ?? (HasFocus ? focused : null))?.Selection;
        session.Interact(row?.Output.Id);
        inspect(row);
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
