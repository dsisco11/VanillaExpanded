using System;
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
    private double composedScale;
    private GuiElementHoverText? tooltip;
    private string? tooltipText;
    private string search = "";
    private bool disposed;

    internal GuiDialogCraftingMenu(ICoreClientAPI api, GuiDialog parent, GuiComposer parentComposer,
        CraftingCatalogue catalogue, CraftingAllocator allocator) : base(api)
    {
        this.parent = parent;
        this.parentComposer = parentComposer;
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

    public override bool TryOpen()
    {
        if (!Follow()) return false;
        Refresh();
        if (disposed) return false;
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
        var content = ElementBounds.Fixed(8, 30, width - 16, height - 38);
        var searchBounds = ElementBounds.Fixed(0, 0, content.fixedWidth, 26);
        var viewport = ElementBounds.Fixed(0, 0, content.fixedWidth - 16, height - 108).FixedUnder(searchBounds, 8);
        var scrollBounds = ElementBounds.Fixed(0, viewport.fixedY, 12, viewport.fixedHeight).FixedRightOf(viewport, 4);
        var footer = ElementBounds.Fixed(0, 0, content.fixedWidth, 24).FixedUnder(viewport, 10);
        var previous = ElementBounds.Fixed(0, 0, 24, 24);
        var auto = ElementBounds.Fixed(0, 0, 46, 24).RightOf(previous, 3);
        var next = ElementBounds.Fixed(0, 0, 24, 24).RightOf(auto, 3);
        var label = ElementBounds.Fixed(0, 2, Math.Max(1, content.fixedWidth - 104), 24).RightOf(next, 4);
        var tiles = new GuiElementCraftingTiles(capi, ElementBounds.Fixed(0, 0, viewport.fixedWidth, viewport.fixedHeight),
            viewport, session, Inspect, Activate);
        var newTooltip = new GuiElementHoverText(capi, "", CairoFont.WhiteSmallText(), 420,
            ElementBounds.Fixed(0, 0, 1, 1));
        newTooltip.SetAutoDisplay(false);
        var composer = capi.Gui.CreateCompo("vanillaexpanded-craftingmenu", bounds)
            .AddShadedDialogBG(ElementBounds.Fill)
            .AddDialogTitleBar(Lang.Get("vanillaexpanded:craftingmenu-title"), CloseWithParent)
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
                ElementBounds.Fixed(0, viewport.fixedY, viewport.fixedWidth, 40), "empty")
            .BeginChildElements(footer)
            .AddSmallButton("<", () => Cycle(-1), previous)
            .AddSmallButton(Lang.Get("vanillaexpanded:craftingmenu-auto"), () => { session.Auto(); return true; },
                auto)
            .AddSmallButton(">", () => Cycle(1), next)
            .AddHoverText(Lang.Get("vanillaexpanded:craftingmenu-alternative-hint"), CairoFont.WhiteSmallText(),
                300, ElementBounds.Fixed(0, 0, 100, 24))
            .AddDynamicText("", CairoFont.WhiteSmallText(), label, "alternative")
            .EndChildElements()
            .AddInteractiveElement(newTooltip, "tooltip")
            .EndChildElements()
            .Compose();
        SingleComposer?.Dispose();
        SingleComposer = composer;
        tooltip = newTooltip;
        tooltipText = null;
        composer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vanillaexpanded:craftingmenu-search"));
        composer.GetTextInput("search").SetValue(search);
        tiles.Bind(composer.GetScrollbar("scroll"));
        UpdateLabels();
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
        var next = CraftingMenuLayout.Place(parentComposer.Bounds, grid, output, capi.Gui.WindowBounds);
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
        SingleComposer.GetDynamicText("alternative").SetNewText(selected == null
            || SingleComposer.Bounds.fixedWidth < 180 ? "" :
            $"{selected.Output.Alternatives.IndexOf(selected.Recipe) + 1}/{selected.Output.Alternatives.Length}"
                + (selected.IsExplicit ? "*" : ""));
    }

    private void Inspect(CraftingRecipeSelection? row)
    {
        if (tooltip == null) return;
        string text = row == null ? "" : CraftingMenuTooltip.Describe(row,
            getTagNames: capi.CollectibleTagRegistry.SlowEnumerateTagNames);
        if (text != tooltipText) { tooltip.SetNewText(text); tooltipText = text; }
        tooltip.SetVisible(row != null);
    }

    private void Error(string reason) => capi.TriggerIngameError(this, "craftingmenu", Lang.Get(reason));
    private void CloseWithParent() { TryClose(); Dispose(); }

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
    }
}
