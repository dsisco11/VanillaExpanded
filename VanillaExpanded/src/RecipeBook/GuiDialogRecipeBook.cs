using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiDialogRecipeBook : GuiDialog
{
    private readonly GuiDialog parent;
    private readonly GuiComposer parentComposer;
    private readonly RecipeBookSession session;
    private readonly CraftingTransferCoordinator coordinator;
    private readonly RecipeBookIconCache icons;
    private double composedScale;
    private double minimumWidth;
    private double minimumHeight;
    private double searchHeight;
    private double craftableHeight;
    private double sortLabelWidth;
    private double sortValueWidth;
    private double tabsHeight;
    private double footerHeight;
    private double previousWidth;
    private double autoWidth;
    private double nextWidth;
    private double handbookWidth;
    private double emptyTextHeight;
    private double clearSearchWidth;
    private double showUncraftableWidth;
    private GuiElementRecipeBookTooltip? tooltip;
    private GuiElementRecipeBookOptions? options;
    private string search = "";
    private float? restoreScroll;
    private bool disposed;
    private readonly HashSet<EnumMouseButton> pressedMouseButtons = [];
    private readonly string?[] categories;
    internal event Action? Disposed;
    internal event Action? CloseRequestedByUser;
    internal RecipeBookBrowsingState BrowsingState { get; private set; }

    internal GuiDialogRecipeBook(ICoreClientAPI api, GuiDialog parent, GuiComposer parentComposer,
        CraftingCatalogue catalogue, CraftingAllocator allocator, RecipeBookIconCache icons,
        RecipeBookBrowsingState? browsingState = null) : base(api)
    {
        this.parent = parent;
        this.parentComposer = parentComposer;
        this.icons = icons;
        categories = [null,
            .. catalogue.CategoryCodes.Where(code => code is not (CraftingCatalogue.OtherCategory
                or "flora" or "clutter" or "aquatic" or "liquids" or "creatures" or "meta"))
                .OrderBy(CategoryName, StringComparer.CurrentCultureIgnoreCase),
            .. catalogue.CategoryCodes.Where(code => code == CraftingCatalogue.OtherCategory)];
        coordinator = new(api, allocator, result => Error(result.Reason!));
        session = new(catalogue, allocator,
            () => CraftingInventorySnapshot.Capture(api.World.Player, 3, 3, RuntimeEnv.MainThreadId, false),
            coordinator.Execute);
        BrowsingState = browsingState ?? new();
        search = BrowsingState.Search;
        restoreScroll = BrowsingState.Scroll;
        coordinator.InventoryChanged += Refresh;
        session.Changed += UpdateLabels;
        parent.OnClosed += CloseWithParent;
        api.Event.LeaveWorld += CloseWithParent;
    }

    public override string ToggleKeyCombinationCode => "";
    public override double DrawOrder => 0.2;
    public override bool PrefersUngrabbedMouse => true;
    public override bool CaptureAllInputs() => Focused && IsOpened()
        && (SingleComposer?.GetTextInput("search")?.HasFocus ?? false);
    public override bool OnEscapePressed()
    {
        if (options?.IsOpened != true) return parent.OnEscapePressed();
        options.CloseMenu();
        return true;
    }

    public override bool TryOpen() => TryOpen(true);

    public override bool TryOpen(bool withFocus)
    {
        long start = Stopwatch.GetTimestamp();
        if (restoreScroll != null)
        {
            session.Search(search);
            session.SelectCategory(BrowsingState.Category);
            session.Sort.Select(BrowsingState.Sort);
        }
        if (!Follow(withFocus)) return false;
        double composeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        Refresh();
        if (disposed) return false;
        if (restoreScroll is float position)
        {
            ((GuiElementRecipeBookTiles)SingleComposer.GetElement("tiles")).Scroll(position * (float)GuiElement.scaled(1));
            restoreScroll = null;
        }
        capi.Logger.Debug("[VanillaExpanded] Recipe book: layout/composition {0:F1} ms, inventory evaluation {1:F1} ms, {2} outputs.",
            composeMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds, session.Rows.Length);
        return base.TryOpen(withFocus);
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

    private void Compose(ElementBounds bounds, bool focusFirstElement)
    {
        float scroll = (SingleComposer?.GetElement("tiles") as GuiElementRecipeBookTiles)?.ScrollPosition ?? 0;
        double width = bounds.fixedWidth;
        double height = bounds.fixedHeight;
        double contentTop = GuiStyle.TitleBarHeight + 8;
        var content = ElementBounds.Fixed(8, contentTop, width - 16, height - contentTop - 8);
        var tabsBounds = ElementBounds.Fixed(0, 0, content.fixedWidth, tabsHeight);
        var craftableBounds = ElementBounds.Fixed(0, tabsHeight + 8,
            craftableHeight, craftableHeight);
        double textHeight = CairoFont.WhiteDetailText().GetFontExtents().Height / GuiElement.scaled(1);
        var craftableLabel = ElementBounds.Fixed(craftableHeight + 8,
            craftableBounds.fixedY + (craftableHeight - textHeight) / 2,
            content.fixedWidth - craftableHeight - 8, textHeight);
        double sortTop = craftableBounds.fixedY + craftableHeight + 8;
        var sortLabel = ElementBounds.Fixed(0, sortTop + (craftableHeight - textHeight) / 2, sortLabelWidth, textHeight);
        var sortBounds = ElementBounds.Fixed(sortLabelWidth + 8, sortTop,
            sortValueWidth, craftableHeight);
        var searchBounds = ElementBounds.Fixed(0, sortTop + craftableHeight + 8, content.fixedWidth, searchHeight);
        var footer = ElementBounds.Fixed(0, content.fixedHeight - footerHeight, content.fixedWidth, footerHeight);
        double listTop = searchBounds.fixedY + searchHeight + 8;
        double listHeight = footer.fixedY - 10 - listTop;
        var listFrame = ElementBounds.Fixed(-3, listTop - 3, content.fixedWidth + 6, listHeight + 6);
        double border = 2 / GuiElement.scaled(1);
        var viewport = ElementBounds.Fixed(0, listFrame.fixedY + border, content.fixedWidth - 16,
            listFrame.fixedHeight - 2 * border);
        var scrollBounds = ElementBounds.Fixed(0, viewport.fixedY, 12, viewport.fixedHeight).FixedRightOf(viewport, 4);
        var previous = ElementBounds.Fixed(0, 0, previousWidth, footerHeight);
        var auto = ElementBounds.Fixed(0, 0, autoWidth, footerHeight).FixedRightOf(previous, 3);
        var next = ElementBounds.Fixed(0, 0, nextWidth, footerHeight).FixedRightOf(auto, 3);
        double labelLeft = next.fixedX + next.fixedWidth + 4;
        var handbook = ElementBounds.Fixed(content.fixedWidth - handbookWidth, 0, handbookWidth, footerHeight);
        var label = ElementBounds.Fixed(labelLeft, 0, handbook.fixedX - 4 - labelLeft, footerHeight);
        var tiles = new GuiElementRecipeBookTiles(capi, ElementBounds.Fixed(0, 0, viewport.fixedWidth, viewport.fixedHeight).WithParent(viewport),
            viewport, session, Inspect, Activate, icons);
        var newTooltip = new GuiElementRecipeBookTooltip(capi);
        var newOptions = new GuiElementRecipeBookOptions(capi, ElementBounds.Fixed(width - 80, 7, 17, 17));
        var composer = capi.Gui.CreateCompo("vanillaexpanded-recipebook", bounds)
            .AddShadedDialogBG(ElementBounds.Fill)
            .AddDialogTitleBar(Lang.Get("vanillaexpanded:recipebook-title"), () => CloseByUser(), key: "titlebar")
            .BeginChildElements(content)
            .AddInteractiveElement(new GuiElementRecipeBookSearch(capi, searchBounds, text =>
            {
                search = text;
                tiles.ResetScroll();
                session.Search(text);
            }), "search")
            .AddInteractiveElement(new GuiElementIconTabs(capi,
                categories.Select((code, index) => new GuiTab { DataInt = index, Name = CategoryName(code) }).ToArray(),
                categories.Select(RecipeBookCategoryIcons.ForCategory).ToArray(), tabsBounds, index =>
                {
                    if (session.Category == categories[index]) return;
                    tiles.ResetScroll();
                    session.SelectCategory(categories[index]);
                }), "categories")
            .AddSwitch(enabled =>
            {
                tiles.ResetScroll();
                session.CraftableOnly.SetEnabled(enabled);
            }, craftableBounds, "craftable-only", size: craftableHeight)
            .AddStaticText(Lang.Get("vanillaexpanded:recipebook-craftable-only"), CairoFont.WhiteDetailText(),
                craftableLabel, "craftable-label")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-craftable-only-hint"), CairoFont.WhiteSmallText(),
                300, ElementBounds.Fixed(0, craftableBounds.fixedY, content.fixedWidth, craftableHeight),
                "craftable-hint")
            .AddStaticText(Lang.Get("vanillaexpanded:recipebook-sort"), CairoFont.WhiteDetailText(),
                sortLabel, "sort-label")
            .AddDropDown(session.Sort.Orders.Select(order => order.Id).ToArray(),
                session.Sort.Orders.Select(order => Lang.Get(order.NameKey)).ToArray(),
                session.Sort.Orders.IndexOf(session.Sort.Selected), (id, _) =>
                {
                    if (session.Sort.Selected.Id == id) return;
                    tiles.ResetScroll();
                    session.Sort.Select(id);
                }, sortBounds, CairoFont.WhiteDetailText(), "sort")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-sort-hint"), CairoFont.WhiteSmallText(),
                300, ElementBounds.Fixed(0, sortTop, sortBounds.fixedX + sortBounds.fixedWidth, craftableHeight),
                "sort-hint")
            .AddStaticElement(new GuiElementInset(capi, listFrame, 2, 0.65f), "recipes-background")
            .BeginClip(viewport)
                .AddInteractiveElement(tiles, "tiles")
            .EndClip()
            .AddVerticalScrollbar(tiles.Scroll, scrollBounds, "scroll")
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(0, viewport.fixedY, viewport.fixedWidth, emptyTextHeight), "empty")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-clear-search"), ClearSearch,
                ElementBounds.Fixed(0, viewport.fixedY + emptyTextHeight + 8, clearSearchWidth, footerHeight),
                key: "clear-search")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-show-uncraftable"), ShowUncraftable,
                ElementBounds.Fixed(0, viewport.fixedY + emptyTextHeight + 8, showUncraftableWidth, footerHeight),
                key: "show-uncraftable")
            .BeginChildElements(footer)
            .AddSmallButton("<", () => Cycle(-1), previous, key: "previous")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-auto"), () => { session.Auto(); return true; },
                auto, key: "auto")
            .AddSmallButton(">", () => Cycle(1), next, key: "next")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-alternative-hint"), CairoFont.WhiteSmallText(),
                300, ElementBounds.Fixed(0, 0, next.fixedX + next.fixedWidth, footerHeight))
            .AddDynamicText("", CairoFont.WhiteSmallText(), label, "alternative")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-handbook"), OpenHandbook, handbook, key: "handbook")
            .EndChildElements()
            .AddInteractiveElement(newTooltip, "tooltip")
            .EndChildElements()
            .AddInteractiveElement(newOptions, "options")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-options"), CairoFont.WhiteSmallText(),
                200, ElementBounds.Fixed(width - 80, 7, 17, 17))
            .Compose(focusFirstElement);
        SingleComposer?.Dispose();
        SingleComposer = composer;
        tooltip = newTooltip;
        options = newOptions;
        composer.GetSwitch("craftable-only").SetValue(session.CraftableOnly.Enabled);
        ((GuiElementIconTabs)composer.GetElement("categories")).SetValue(Array.IndexOf(categories, session.Category), false);
        composer.GetTextInput("search").SetPlaceHolderText(Lang.Get("vanillaexpanded:recipebook-search"));
        composer.GetTextInput("search").SetValue(search);
        tiles.Bind(composer.GetScrollbar("scroll"));
        tiles.Scroll(scroll * (float)GuiElement.scaled(1));
        UpdateLabels();
    }

    private static string CategoryName(string? code) => code switch
    {
        null => Lang.Get("vanillaexpanded:recipebook-category-all"),
        CraftingCatalogue.OtherCategory => Lang.Get("vanillaexpanded:recipebook-category-other"),
        _ => Lang.GetIfExists("tabname-" + code) ?? code
    };

    private void MeasureLayout()
    {
        double scale = GuiElement.scaled(1);
        var buttonFont = CairoFont.SmallButtonText();
        var textFont = CairoFont.WhiteSmallText();
        var filterFont = CairoFont.WhiteDetailText();
        previousWidth = Math.Max(24, Math.Ceiling(buttonFont.GetTextExtents("<").Width / scale + 1));
        autoWidth = Math.Max(46, Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-auto")).Width / scale + 1));
        nextWidth = Math.Max(24, Math.Ceiling(buttonFont.GetTextExtents(">").Width / scale + 1));
        handbookWidth = Math.Max(70, Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-handbook")).Width / scale + 1));
        clearSearchWidth = Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-clear-search")).Width / scale + 1);
        showUncraftableWidth = Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-show-uncraftable")).Width / scale + 1);
        footerHeight = Math.Max(24, Math.Ceiling(Math.Max(buttonFont.GetFontExtents().Height,
            textFont.GetFontExtents().Height) / scale));
        searchHeight = Math.Max(26, Math.Ceiling(textFont.GetFontExtents().Height / scale + 6));
        craftableHeight = Math.Max(20, Math.Ceiling(filterFont.GetFontExtents().Height / scale + 4));
        tabsHeight = GuiElementIconTabs.TabSize + 2;
        double counterWidth = Math.Ceiling(textFont.GetTextExtents("1/1").Width / scale + 1);
        double titleWidth = textFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-title")).Width / scale;
        // The native title bar reserves space for its menu and close icons.
        double titleControlsWidth = 2 * GuiElementDialogTitleBar.unscaledCloseIconSize + 2 + 20 + 30;
        double craftableWidth = 16 + craftableHeight + 8 + filterFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-craftable-only")).Width / scale;
        sortLabelWidth = Math.Ceiling(filterFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-sort")).Width / scale + 1);
        sortValueWidth = Math.Ceiling(40 + session.Sort.Orders
            .Max(order => filterFont.GetTextExtents(Lang.Get(order.NameKey)).Width / scale));
        double sortWidth = 16 + sortLabelWidth + 8 + sortValueWidth;
        double optionsWidth = textFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-remember-open")).Width / scale + 120;
        minimumWidth = Math.Ceiling(Math.Max(optionsWidth, Math.Max(sortWidth, Math.Max(32 + Math.Max(clearSearchWidth, showUncraftableWidth),
            Math.Max(craftableWidth, Math.Max(
            GuiStyle.ElementToDialogPadding + titleWidth + 8 + titleControlsWidth,
            16 + previousWidth + 3 + autoWidth + 3 + nextWidth + 4 + counterWidth + 4 + handbookWidth))))));
        var textUtil = new TextDrawUtil();
        emptyTextHeight = Math.Ceiling(Enum.GetValues<RecipeBookEmptyState>()
            .Where(state => state != RecipeBookEmptyState.None)
            .Max(state => textUtil.GetMultilineTextHeight(textFont, Lang.Get(EmptyMessageKey(state)),
                (minimumWidth - 32) * scale)) / scale);
        minimumHeight = Math.Ceiling(GuiStyle.TitleBarHeight + 8 + tabsHeight + 8 + craftableHeight + 8
            + craftableHeight + 8 + searchHeight + 8
            + Math.Max(GuiElementRecipeBookTiles.TileSize, emptyTextHeight + 8 + footerHeight) + 10 + footerHeight + 8);
    }

    private bool Follow(bool focusFirstElement)
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
        var next = RecipeBookLayout.Place(parentComposer.Bounds, grid, output, capi.Gui.WindowBounds,
            minimumWidth, minimumHeight);
        if (next == null)
        {
            Error("vanillaexpanded:recipebook-screen-space");
            CloseWithParent();
            return false;
        }
        if (SingleComposer == null || composedScale != RuntimeEnv.GUIScale
            || SingleComposer.Bounds.fixedWidth != next.fixedWidth || SingleComposer.Bounds.fixedHeight != next.fixedHeight)
        {
            Compose(next, focusFirstElement);
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
        if (disposed || !Follow(Focused)) return;
        base.OnRenderGUI(deltaTime);
    }

    public override void OnMouseMove(MouseEvent args)
    {
        options?.OnMouseMove(capi, args);
        if (args.Handled) return;
        (SingleComposer?.GetElement("tiles") as GuiElementRecipeBookTiles)?.UpdateHover(args.X, args.Y);
        base.OnMouseMove(args);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (args.Handled) return;
        if (options?.IsOpened == true) options.OnMouseDown(capi, args);
        if (!args.Handled) base.OnMouseDown(args);
        if (args.Handled) pressedMouseButtons.Add(args.Button);
        if (options?.HasFocus == true) tooltip?.Inspect(null);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (!pressedMouseButtons.Remove(args.Button)) return;
        base.OnMouseUp(args);
        if (options?.IsOpened == true) tooltip?.Inspect(null);
    }

    public override void OnKeyDown(KeyEvent args)
    {
        if (args.Handled) return;
        base.OnKeyDown(args);
        if (args.Handled || !IsOpened()) return;
        var hotkey = capi.Input.GetHotKeyByCode(parent.ToggleKeyCombinationCode);
        if (hotkey != null && hotkey.DidPress(args, capi.World, capi.World.Player, true))
            args.Handled = parent.TryClose();
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

    private bool OpenHandbook()
    {
        var stack = session.Rows.FirstOrDefault(row => row.Output.Id == session.SelectedOutput)?.Recipe.GetPreview();
        if (stack == null) return true;
        if (capi.LinkProtocols == null || !capi.LinkProtocols.TryGetValue("handbook", out var open))
        {
            Error("vanillaexpanded:recipebook-handbook-unavailable");
            return true;
        }
        string pageCode = stack.Collectible.GetCollectibleInterface<IHandBookPageCodeProvider>()
            ?.HandbookPageCodeForStack(capi.World, stack) ?? GuiHandbookItemStackPage.PageCodeForStack(stack);
        open(new LinkTextComponent("handbook://" + pageCode));
        var handbook = capi.Gui.LoadedGuis.FirstOrDefault(dialog =>
            dialog.IsOpened() && dialog.ToggleKeyCombinationCode == "handbook");
        if (handbook != null) capi.Gui.RequestFocus(handbook);
        return true;
    }

    private bool ClearSearch()
    {
        if (session.EmptyState != RecipeBookEmptyState.NoSearchMatches) return true;
        var input = SingleComposer.GetTextInput("search");
        input.SetValue("");
        SingleComposer.FocusElement(input.TabIndex);
        return true;
    }

    private bool ShowUncraftable()
    {
        if (session.EmptyState is not (RecipeBookEmptyState.NoCraftableRecipes or RecipeBookEmptyState.NoCraftableSearchMatches))
            return true;
        ((GuiElementRecipeBookTiles)SingleComposer.GetElement("tiles")).ResetScroll();
        SingleComposer.GetSwitch("craftable-only").SetValue(false);
        session.CraftableOnly.SetEnabled(false);
        return true;
    }

    private static string EmptyMessageKey(RecipeBookEmptyState state) => state switch
    {
        RecipeBookEmptyState.NoRecipes => "vanillaexpanded:recipebook-empty",
        RecipeBookEmptyState.NoSearchMatches => "vanillaexpanded:recipebook-empty-search",
        RecipeBookEmptyState.NoCraftableRecipes => "vanillaexpanded:recipebook-empty-craftable",
        RecipeBookEmptyState.NoCraftableSearchMatches => "vanillaexpanded:recipebook-empty-craftable-search",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private void UpdateLabels()
    {
        if (SingleComposer == null) return;
        (string Key, bool Visible)[] actions =
        {
            ("clear-search", session.EmptyState == RecipeBookEmptyState.NoSearchMatches),
            ("show-uncraftable", session.EmptyState is RecipeBookEmptyState.NoCraftableRecipes
                or RecipeBookEmptyState.NoCraftableSearchMatches)
        };
        var viewport = SingleComposer.GetElement("tiles").InsideClipBounds;
        var empty = SingleComposer.GetDynamicText("empty");
        string message = session.EmptyState == RecipeBookEmptyState.None ? "" : Lang.Get(EmptyMessageKey(session.EmptyState));
        double scale = GuiElement.scaled(1);
        double textHeight = Math.Max(1, Math.Ceiling(new TextDrawUtil()
            .GetMultilineTextHeight(empty.Font, message, viewport.InnerWidth) / scale));
        double actionHeight = actions.Where(action => action.Visible)
            .Select(action => SingleComposer.GetButton(action.Key).Bounds.fixedHeight + 8).DefaultIfEmpty(0).Max();
        bool resized = empty.Bounds.fixedHeight != textHeight;
        empty.Bounds.fixedHeight = textHeight;
        empty.Bounds.fixedY = viewport.fixedY + (viewport.fixedHeight - textHeight - actionHeight) / 2;
        empty.Bounds.MarkDirtyRecursive();
        empty.Bounds.CalcWorldBounds();
        empty.SetNewText(message, forceRedraw: resized);
        foreach (var (key, visible) in actions)
        {
            var button = SingleComposer.GetButton(key);
            if (!visible && button.HasFocus) button.OnFocusLost();
            button.Enabled = button.Visible = visible;
            if (!visible) continue;
            button.Bounds.fixedX = viewport.fixedX + (viewport.fixedWidth - button.Bounds.fixedWidth) / 2;
            button.Bounds.fixedY = empty.Bounds.fixedY + textHeight + 8;
            button.Bounds.MarkDirtyRecursive();
            button.Bounds.CalcWorldBounds();
        }
        var selected = session.Rows.FirstOrDefault(row => row.Output.Id == session.SelectedOutput);
        foreach (string key in new[] { "previous", "auto", "next", "handbook" })
        {
            var button = SingleComposer.GetButton(key);
            bool enabled = selected != null && (key != "handbook" || selected.Recipe.PreviewId != null);
            if (!enabled && button.HasFocus) button.OnFocusLost();
            button.Enabled = enabled;
        }
        SingleComposer.GetDynamicText("alternative").SetNewText(selected == null ? "" :
            $"{selected.Output.Alternatives.IndexOf(selected.Recipe) + 1}/{selected.Output.Alternatives.Length}");
    }

    private void Inspect(CraftingRecipeSelection? row) => tooltip?.Inspect(row);

    private void Error(string reason) => capi.TriggerIngameError(this, "recipebook", Lang.Get(reason));
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
        if (SingleComposer?.GetElement("tiles") is GuiElementRecipeBookTiles tiles)
            BrowsingState = new(session.Category, search, tiles.ScrollPosition, session.Sort.Selected.Id);
        parent.OnClosed -= CloseWithParent;
        capi.Event.LeaveWorld -= CloseWithParent;
        coordinator.InventoryChanged -= Refresh;
        coordinator.Dispose();
        session.Changed -= UpdateLabels;
        capi.Gui.LoadedGuis.Remove(this);
        base.Dispose();
        session.Dispose();
        Disposed?.Invoke();
        Disposed = null;
        CloseRequestedByUser = null;
    }
}
