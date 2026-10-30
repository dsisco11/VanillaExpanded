using System;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal sealed class GuiDialogCraftingMenu : GuiDialog
{
    private readonly GuiDialog parent;
    private readonly GuiComposer parentComposer;
    private readonly CraftingMenuSession session;
    private readonly CraftingTransferCoordinator coordinator;
    private readonly CraftingIconCache icons;
    private double composedScale;
    private double minimumWidth;
    private double minimumHeight;
    private double searchHeight;
    private double footerHeight;
    private double previousWidth;
    private double autoWidth;
    private double nextWidth;
    private GuiElementCraftingRecipeTooltip? tooltip;
    private string search = "";
    private bool disposed;
    internal event Action? Disposed;
    internal event Action? CloseRequestedByUser;

    internal GuiDialogCraftingMenu(ICoreClientAPI api, GuiDialog parent, GuiComposer parentComposer,
        CraftingCatalogue catalogue, CraftingAllocator allocator, CraftingIconCache icons) : base(api)
    {
        this.parent = parent;
        this.parentComposer = parentComposer;
        this.icons = icons;
        coordinator = new(api, allocator, result => Error(result.Reason!));
        session = new(catalogue, allocator,
            () => CraftingInventorySnapshot.Capture(api.World.Player, 3, 3, RuntimeEnv.MainThreadId, false),
            coordinator.Execute);
        coordinator.InventoryChanged += Refresh;
        session.Changed += UpdateLabels;
        parent.OnClosed += CloseWithParent;
        api.Event.LeaveWorld += CloseWithParent;
    }

    public override string ToggleKeyCombinationCode => "";
    public override double DrawOrder => 0.21;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => IsOpened() && (SingleComposer?.GetTextInput("search")?.HasFocus ?? false);
    public override bool OnEscapePressed() => CloseByUser();

    public override bool TryOpen()
    {
        long start = Stopwatch.GetTimestamp();
        if (!Follow()) return false;
        double composeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        Refresh();
        if (disposed) return false;
        capi.Logger.Debug("[VanillaExpanded] Crafting menu: layout/composition {0:F1} ms, inventory evaluation {1:F1} ms, {2} outputs.",
            composeMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds, session.Rows.Length);
        return base.TryOpen();
    }

    internal void NativeGridDirty() { if (IsOpened()) coordinator.QueueRefresh(); }

    private void Refresh()
    {
        if (disposed) return;
        try { session.Refresh(); }
        catch (NotSupportedException ex)
        {
            capi.Logger.Warning("[VanillaExpanded] Crafting preview inventory is unsupported: {0}", ex.Message);
            Error(CraftingTransferPlanner.Unsafe);
            CloseWithParent();
        }
        catch (InvalidOperationException ex)
        {
            capi.Logger.Warning("[VanillaExpanded] Crafting preview inventory changed: {0}", ex.Message);
            Error(CraftingTransferCoordinator.Stale);
            CloseWithParent();
        }
    }

    private void Compose(ElementBounds bounds)
    {
        double width = bounds.fixedWidth;
        double height = bounds.fixedHeight;
        double contentTop = GuiStyle.TitleBarHeight + 8;
        var content = ElementBounds.Fixed(8, contentTop, width - 16, height - contentTop - 8);
        var searchBounds = ElementBounds.Fixed(0, 0, content.fixedWidth, searchHeight);
        var footer = ElementBounds.Fixed(0, content.fixedHeight - footerHeight, content.fixedWidth, footerHeight);
        var viewport = ElementBounds.Fixed(0, searchHeight + 8, content.fixedWidth - 16,
            footer.fixedY - 10 - searchHeight - 8);
        var scrollBounds = ElementBounds.Fixed(0, viewport.fixedY, 12, viewport.fixedHeight).FixedRightOf(viewport, 4);
        var previous = ElementBounds.Fixed(0, 0, previousWidth, footerHeight);
        var auto = ElementBounds.Fixed(0, 0, autoWidth, footerHeight).FixedRightOf(previous, 3);
        var next = ElementBounds.Fixed(0, 0, nextWidth, footerHeight).FixedRightOf(auto, 3);
        double labelLeft = next.fixedX + next.fixedWidth + 4;
        var label = ElementBounds.Fixed(labelLeft, 0, content.fixedWidth - labelLeft, footerHeight);
        var tiles = new GuiElementCraftingTiles(capi, ElementBounds.Fixed(0, 0, viewport.fixedWidth, viewport.fixedHeight),
            viewport, session, Inspect, Activate, icons);
        var newTooltip = new GuiElementCraftingRecipeTooltip(capi);
        var composer = capi.Gui.CreateCompo("vanillaexpanded-craftingmenu", bounds)
            .AddShadedDialogBG(ElementBounds.Fill)
            .AddDialogTitleBar(Lang.Get("vanillaexpanded:craftingmenu-title"), () => CloseByUser(), key: "titlebar")
            .BeginChildElements(content)
            .AddTextInput(searchBounds, text =>
            {
                search = text;
                session.Search(text);
            }, CairoFont.WhiteSmallText(), "search")
            .BeginClip(viewport)
                .AddInteractiveElement(tiles, "tiles")
            .EndClip()
            .AddVerticalScrollbar(tiles.Scroll, scrollBounds, "scroll")
            .AddDynamicText("", CairoFont.WhiteSmallText(),
                ElementBounds.Fixed(0, viewport.fixedY, viewport.fixedWidth, Math.Min(40, viewport.fixedHeight)), "empty")
            .BeginChildElements(footer)
            .AddSmallButton("<", () => Cycle(-1), previous, key: "previous")
            .AddSmallButton(Lang.Get("vanillaexpanded:craftingmenu-auto"), () => { session.Auto(); return true; },
                auto, key: "auto")
            .AddSmallButton(">", () => Cycle(1), next, key: "next")
            .AddHoverText(Lang.Get("vanillaexpanded:craftingmenu-alternative-hint"), CairoFont.WhiteSmallText(),
                300, ElementBounds.Fixed(0, 0, next.fixedX + next.fixedWidth, footerHeight))
            .AddDynamicText("", CairoFont.WhiteSmallText(), label, "alternative")
            .EndChildElements()
            .AddInteractiveElement(newTooltip, "tooltip")
            .EndChildElements()
            .Compose();
        SingleComposer?.Dispose();
        SingleComposer = composer;
        tooltip = newTooltip;
        composer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vanillaexpanded:craftingmenu-search"));
        composer.GetTextInput("search").SetValue(search);
        tiles.Bind(composer.GetScrollbar("scroll"));
        UpdateLabels();
    }

    private void MeasureLayout()
    {
        double scale = GuiElement.scaled(1);
        var buttonFont = CairoFont.SmallButtonText();
        var textFont = CairoFont.WhiteSmallText();
        previousWidth = Math.Max(24, Math.Ceiling(buttonFont.GetTextExtents("<").Width / scale + 1));
        autoWidth = Math.Max(46, Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:craftingmenu-auto")).Width / scale + 1));
        nextWidth = Math.Max(24, Math.Ceiling(buttonFont.GetTextExtents(">").Width / scale + 1));
        footerHeight = Math.Max(24, Math.Ceiling(Math.Max(buttonFont.GetFontExtents().Height,
            textFont.GetFontExtents().Height) / scale));
        searchHeight = Math.Max(26, Math.Ceiling(textFont.GetFontExtents().Height / scale + 6));
        double counterWidth = Math.Ceiling(textFont.GetTextExtents("1/1").Width / scale + 1);
        double titleWidth = textFont.GetTextExtents(Lang.Get("vanillaexpanded:craftingmenu-title")).Width / scale;
        // The native title bar reserves space for its menu and close icons.
        double titleControlsWidth = 2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20;
        minimumWidth = Math.Ceiling(Math.Max(
            GuiStyle.ElementToDialogPadding + titleWidth + 8 + titleControlsWidth,
            16 + previousWidth + 3 + autoWidth + 3 + nextWidth + 4 + counterWidth));
        minimumHeight = Math.Ceiling(GuiStyle.TitleBarHeight + 8 + searchHeight + 8
            + GuiElementCraftingTiles.TileSize + 10 + footerHeight + 8);
    }

    private bool Follow()
    {
        if (!parent.IsOpened() || !parent.Composers.ContainsKey("maininventory")
            || !ReferenceEquals(parent.Composers["maininventory"], parentComposer))
        {
            CloseWithParent();
            return false;
        }
        var grid = parentComposer.GetElement("craftinggrid")?.Bounds;
        if (grid == null) { CloseWithParent(); return false; }
        var output = parentComposer.GetElement("outputslot").Bounds;
        if (SingleComposer == null || composedScale != RuntimeEnv.GUIScale) MeasureLayout();
        var next = CraftingMenuLayout.Place(parentComposer.Bounds, grid, output, capi.Gui.WindowBounds,
            minimumWidth, minimumHeight);
        if (next == null)
        {
            Error("vanillaexpanded:craftingmenu-screen-space");
            CloseWithParent();
            return false;
        }
        if (SingleComposer == null || composedScale != RuntimeEnv.GUIScale
            || SingleComposer.Bounds.fixedWidth != next.fixedWidth || SingleComposer.Bounds.fixedHeight != next.fixedHeight)
        {
            Compose(next);
            composedScale = RuntimeEnv.GUIScale;
        }
        else
        {
            SingleComposer.Bounds.fixedX = next.fixedX;
            SingleComposer.Bounds.fixedY = next.fixedY;
            SingleComposer.Bounds.MarkDirtyRecursive();
            SingleComposer.Bounds.CalcWorldBounds();
        }
        return true;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (capi.Render.FrameWidth == 0 || capi.Render.FrameHeight == 0) return;
        if (disposed || !Follow()) return;
        base.OnRenderGUI(deltaTime);
    }

    public override void OnMouseMove(MouseEvent args)
    {
        (SingleComposer?.GetElement("tiles") as GuiElementCraftingTiles)?.UpdateHover(args.X, args.Y);
        base.OnMouseMove(args);
    }

    private void Activate(int output, bool shift)
    {
        var result = session.Activate(output, shift);
        if (result.Status == CraftingDispatchStatus.Rejected && !session.Rows.First(row => row.Output.Id == output).CanPlace)
            Error(result.Reason!);
        UpdateLabels();
    }

    private bool Cycle(int direction)
    {
        if (session.SelectedOutput is not int output) return true;
        var selection = session.Rows.FirstOrDefault(row => row.Output.Id == output);
        if (selection == null) return true;
        int index = selection.Output.Alternatives.IndexOf(selection.Recipe);
        session.ChooseAlternative(selection.Output.Alternatives[
            (index + direction + selection.Output.Alternatives.Length) % selection.Output.Alternatives.Length].Id);
        return true;
    }

    private void UpdateLabels()
    {
        if (SingleComposer == null) return;
        SingleComposer.GetDynamicText("empty").SetNewText(session.Rows.IsEmpty
            ? Lang.Get("vanillaexpanded:craftingmenu-empty") : "");
        var selected = session.Rows.FirstOrDefault(row => row.Output.Id == session.SelectedOutput);
        SingleComposer.GetDynamicText("alternative").SetNewText(selected == null ? "" :
            $"{selected.Output.Alternatives.IndexOf(selected.Recipe) + 1}/{selected.Output.Alternatives.Length}");
    }

    private void Inspect(CraftingRecipeSelection? row) => tooltip?.Inspect(row);

    private void Error(string reason) => capi.TriggerIngameError(this, "craftingmenu", Lang.Get(reason));
    private bool CloseByUser()
    {
        if (IsOpened()) CloseRequestedByUser?.Invoke();
        return TryClose();
    }

    internal void CloseWithParent() { TryClose(); Dispose(); }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        Dispose();
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        parent.OnClosed -= CloseWithParent;
        capi.Event.LeaveWorld -= CloseWithParent;
        coordinator.InventoryChanged -= Refresh;
        coordinator.Dispose();
        session.Changed -= UpdateLabels;
        capi.Gui.LoadedGuis.Remove(this);
        base.Dispose();
        Disposed?.Invoke();
        Disposed = null;
        CloseRequestedByUser = null;
    }
}
