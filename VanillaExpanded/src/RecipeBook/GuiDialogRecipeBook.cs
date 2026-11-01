using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiDialogRecipeBook : GuiDialog
{
    private const string ComposerName = "vanillaexpanded-recipebook";
    private const double DialogPadding = 8;
    private const double PanelPadding = 8;
    private const double ContentInset = DialogPadding + PanelPadding;
    private const double RowSpacing = 8;
    private const double LabelSpacing = 8;
    private const double CategoryTitleTopPadding = 12;
    private const float CategoryTitleFontSize = 11;
    private const double ListFrameOutset = 3;
    private const double ListFooterSpacing = 10;
    private const int ListBorderPixels = 2;
    private static readonly string[] StandardCategories =
        ["construction", "decorative", "mechanics", "terrain", "tools", "clothing", "items"];
    private const float ListBackgroundBrightness = 0.65f;
    private const double ScrollbarWidth = 12;
    private const double ScrollbarSpacing = 4;
    private const double ScrollbarSpace = ScrollbarWidth + ScrollbarSpacing;
    private const double FooterButtonSpacing = 3;
    private const double FooterLabelSpacing = 4;
    private const double HandbookTextPadding = 8;
    private const double MinimumNavigationWidth = 24;
    private const double MinimumAutoWidth = 46;
    private const double MinimumHandbookWidth = 70;
    private const double MinimumFooterHeight = 24;
    private const double MinimumSearchHeight = 26;
    private const double SearchTextPadding = 3;
    private const double MinimumFilterHeight = 20;
    private const double FilterTextPadding = 2;
    private const double TextWidthAllowance = 1;
    private const int HintMaxWidth = 300;
    private const double OptionsIconTop = 7;
    private const double OptionsIconSpacing = 11;
    private const double OptionsTitlebarReserve = 30;
    // The native controls expose their icon size, but not these chrome dimensions.
    private const double NativeTitlebarRightSpacing = 20;
    private const double NativeMenuIconExpansion = 2;
    private const double NativeDropdownChromeWidth = 40;
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
    private double categoryTitleHeight;
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
                .OrderBy(code =>
                {
                    int index = Array.IndexOf(StandardCategories, code);
                    return index >= 0 ? index : StandardCategories.Length;
                })
                .ThenBy(CategoryName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(code => code, StringComparer.Ordinal),
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
        double contentTop = GuiStyle.TitleBarHeight + DialogPadding;
        var content = ElementBounds.Fixed(ContentInset, contentTop, width - 2 * ContentInset, height - contentTop - ContentInset);
        var tabsBounds = ElementBounds.Fixed(-PanelPadding, 0, content.fixedWidth + 2 * PanelPadding, tabsHeight);
        double panelTop = tabsHeight - Math.Max(1,
            GuiControlDrawing.Pixels(GuiControlMetrics.Outline, GuiElement.scaled(1))) / GuiElement.scaled(1);
        var categoryPanel = ElementBounds.Fixed(-PanelPadding, panelTop, content.fixedWidth + 2 * PanelPadding,
            content.fixedHeight - panelTop + PanelPadding);
        var categoryTitle = ElementBounds.Fixed(0, tabsHeight + CategoryTitleTopPadding, content.fixedWidth, categoryTitleHeight);
        var craftableBounds = ElementBounds.Fixed(0, categoryTitle.fixedY + categoryTitleHeight + RowSpacing,
            craftableHeight, craftableHeight);
        double textHeight = CairoFont.WhiteDetailText().GetFontExtents().Height / GuiElement.scaled(1);
        var craftableLabel = ElementBounds.Fixed(craftableHeight + LabelSpacing,
            craftableBounds.fixedY + (craftableHeight - textHeight) / 2,
            content.fixedWidth - craftableHeight - LabelSpacing, textHeight);
        double sortTop = craftableBounds.fixedY + craftableHeight + RowSpacing;
        var sortLabel = ElementBounds.Fixed(0, sortTop + (craftableHeight - textHeight) / 2, sortLabelWidth, textHeight);
        var sortBounds = ElementBounds.Fixed(sortLabelWidth + LabelSpacing, sortTop,
            sortValueWidth, craftableHeight);
        var searchBounds = ElementBounds.Fixed(0, sortTop + craftableHeight + RowSpacing, content.fixedWidth, searchHeight);
        var footer = ElementBounds.Fixed(0, content.fixedHeight - footerHeight, content.fixedWidth, footerHeight);
        double listTop = searchBounds.fixedY + searchHeight + RowSpacing;
        double listHeight = footer.fixedY - ListFooterSpacing - listTop;
        var listFrame = ElementBounds.Fixed(-ListFrameOutset, listTop - ListFrameOutset,
            content.fixedWidth + 2 * ListFrameOutset, listHeight + 2 * ListFrameOutset);
        double border = ListBorderPixels / GuiElement.scaled(1);
        var viewport = ElementBounds.Fixed(0, listFrame.fixedY + border, content.fixedWidth - ScrollbarSpace,
            listFrame.fixedHeight - 2 * border);
        var scrollBounds = ElementBounds.Fixed(0, viewport.fixedY, ScrollbarWidth, viewport.fixedHeight)
            .FixedRightOf(viewport, ScrollbarSpacing);
        var previous = ElementBounds.Fixed(0, 0, previousWidth, footerHeight);
        var auto = ElementBounds.Fixed(0, 0, autoWidth, footerHeight).FixedRightOf(previous, FooterButtonSpacing);
        var next = ElementBounds.Fixed(0, 0, nextWidth, footerHeight).FixedRightOf(auto, FooterButtonSpacing);
        double labelLeft = next.fixedX + next.fixedWidth + FooterLabelSpacing;
        var handbook = ElementBounds.Fixed(content.fixedWidth - handbookWidth, 0, handbookWidth, footerHeight);
        var label = ElementBounds.Fixed(labelLeft, 0, handbook.fixedX - FooterLabelSpacing - labelLeft, footerHeight);
        var tiles = new GuiElementRecipeBookTiles(capi, ElementBounds.Fixed(0, 0, viewport.fixedWidth, viewport.fixedHeight).WithParent(viewport),
            viewport, session, Inspect, Activate, icons);
        var newTooltip = new GuiElementRecipeBookTooltip(capi);
        var newOptions = new GuiElementRecipeBookOptions(capi, ElementBounds.Fixed(width - OptionsRightInset,
            OptionsIconTop, GuiElementRecipeBookOptions.IconSize, GuiElementRecipeBookOptions.IconSize));
        var composer = capi.Gui.CreateCompo(ComposerName, bounds)
            .AddShadedDialogBG(ElementBounds.Fill)
            .AddDialogTitleBar(Lang.Get("vanillaexpanded:recipebook-title"), () => CloseByUser(), key: "titlebar")
            .BeginChildElements(content)
            .AddStaticElement(new GuiElementCustomDraw(capi, categoryPanel, GuiElementIconTab.DrawPanel), "category-panel")
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
            .AddDynamicText(CategoryName(session.Category), CategoryTitleFont(), categoryTitle, "category-title")
            .AddSwitch(enabled =>
            {
                tiles.ResetScroll();
                session.CraftableOnly.SetEnabled(enabled);
            }, craftableBounds, "craftable-only", size: craftableHeight)
            .AddStaticText(Lang.Get("vanillaexpanded:recipebook-craftable-only"), CairoFont.WhiteDetailText(),
                craftableLabel, "craftable-label")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-craftable-only-hint"), CairoFont.WhiteSmallText(),
                HintMaxWidth, ElementBounds.Fixed(0, craftableBounds.fixedY, content.fixedWidth, craftableHeight),
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
                HintMaxWidth, ElementBounds.Fixed(0, sortTop, sortBounds.fixedX + sortBounds.fixedWidth, craftableHeight),
                "sort-hint")
            .AddStaticElement(new GuiElementInset(capi, listFrame, ListBorderPixels, ListBackgroundBrightness), "recipes-background")
            .BeginClip(viewport)
                .AddInteractiveElement(tiles, "tiles")
            .EndClip()
            .AddVerticalScrollbar(tiles.Scroll, scrollBounds, "scroll")
            .AddDynamicText("", CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(0, viewport.fixedY, viewport.fixedWidth, emptyTextHeight), "empty")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-clear-search"), ClearSearch,
                ElementBounds.Fixed(0, viewport.fixedY + emptyTextHeight + RowSpacing, clearSearchWidth, footerHeight),
                key: "clear-search")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-show-uncraftable"), ShowUncraftable,
                ElementBounds.Fixed(0, viewport.fixedY + emptyTextHeight + RowSpacing, showUncraftableWidth, footerHeight),
                key: "show-uncraftable")
            .BeginChildElements(footer)
            .AddSmallButton("<", () => Cycle(-1), previous, key: "previous")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-auto"), () => { session.Auto(); return true; },
                auto, key: "auto")
            .AddSmallButton(">", () => Cycle(1), next, key: "next")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-alternative-hint"), CairoFont.WhiteSmallText(),
                HintMaxWidth, ElementBounds.Fixed(0, 0, next.fixedX + next.fixedWidth, footerHeight))
            .AddDynamicText("", CairoFont.WhiteSmallText(), label, "alternative")
            .AddSmallButton(Lang.Get("vanillaexpanded:recipebook-handbook"), OpenHandbook, handbook, key: "handbook")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-handbook-hint"), CairoFont.WhiteSmallText(),
                HintMaxWidth, ElementBounds.Fixed(handbook.fixedX, 0, handbook.fixedWidth, footerHeight), "handbook-hint")
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

    private static double NativeTitlebarControlsWidth =>
        2 * GuiElementDialogTitleBar.unscaledCloseIconSize + NativeMenuIconExpansion + NativeTitlebarRightSpacing;

    private static double OptionsRightInset =>
        NativeTitlebarControlsWidth + OptionsIconSpacing + GuiElementRecipeBookOptions.IconSize;

    private static CairoFont CategoryTitleFont() => CairoFont.WhiteDetailText().WithFontSize(CategoryTitleFontSize)
        .WithColor([200 / 255d, 184 / 255d, 151 / 255d, 1]);

    private void MeasureLayout()
    {
        double scale = GuiElement.scaled(1);
        var buttonFont = CairoFont.SmallButtonText();
        var textFont = CairoFont.WhiteSmallText();
        var filterFont = CairoFont.WhiteDetailText();
        var categoryFont = CategoryTitleFont();
        previousWidth = Math.Max(MinimumNavigationWidth, Math.Ceiling(buttonFont.GetTextExtents("<").Width / scale + TextWidthAllowance));
        autoWidth = Math.Max(MinimumAutoWidth, Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-auto")).Width / scale + TextWidthAllowance));
        nextWidth = Math.Max(MinimumNavigationWidth, Math.Ceiling(buttonFont.GetTextExtents(">").Width / scale + TextWidthAllowance));
        handbookWidth = Math.Max(MinimumHandbookWidth, Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-handbook")).Width / scale + 2 * HandbookTextPadding + TextWidthAllowance));
        clearSearchWidth = Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-clear-search")).Width / scale + TextWidthAllowance);
        showUncraftableWidth = Math.Ceiling(buttonFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-show-uncraftable")).Width / scale + TextWidthAllowance);
        footerHeight = Math.Max(MinimumFooterHeight, Math.Ceiling(Math.Max(buttonFont.GetFontExtents().Height,
            textFont.GetFontExtents().Height) / scale));
        searchHeight = Math.Max(MinimumSearchHeight, Math.Ceiling(textFont.GetFontExtents().Height / scale + 2 * SearchTextPadding));
        craftableHeight = Math.Max(MinimumFilterHeight, Math.Ceiling(filterFont.GetFontExtents().Height / scale + 2 * FilterTextPadding));
        tabsHeight = GuiElementIconTabs.TabHeight;
        categoryTitleHeight = Math.Ceiling(categoryFont.GetFontExtents().Height / scale);
        double counterWidth = Math.Ceiling(textFont.GetTextExtents("1/1").Width / scale + TextWidthAllowance);
        double titleWidth = textFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-title")).Width / scale;
        double titleControlsWidth = NativeTitlebarControlsWidth + OptionsTitlebarReserve;
        double craftableWidth = 2 * ContentInset + craftableHeight + LabelSpacing + filterFont.GetTextExtents(
            Lang.Get("vanillaexpanded:recipebook-craftable-only")).Width / scale;
        sortLabelWidth = Math.Ceiling(filterFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-sort")).Width / scale + TextWidthAllowance);
        sortValueWidth = Math.Ceiling(NativeDropdownChromeWidth + session.Sort.Orders
            .Max(order => filterFont.GetTextExtents(Lang.Get(order.NameKey)).Width / scale));
        double sortWidth = 2 * ContentInset + sortLabelWidth + LabelSpacing + sortValueWidth;
        double optionsWidth = OptionsRightInset + GuiElementRecipeBookOptions.MeasurePopupWidth(textFont,
            Lang.Get("vanillaexpanded:recipebook-remember-open"));
        double tabsWidth = 2 * DialogPadding + GuiElementIconTabs.MeasureWidth(categories.Length);
        double categoryWidth = 2 * ContentInset
            + categories.Max(code => categoryFont.GetTextExtents(CategoryName(code)).Width / scale + TextWidthAllowance);
        double viewportWidthReserve = 2 * ContentInset + ScrollbarSpace;
        double emptyActionWidth = viewportWidthReserve + Math.Max(clearSearchWidth, showUncraftableWidth);
        double titleMinimumWidth = GuiStyle.ElementToDialogPadding + titleWidth + LabelSpacing + titleControlsWidth;
        double footerWidth = 2 * ContentInset + previousWidth + autoWidth + nextWidth
            + 2 * FooterButtonSpacing + 2 * FooterLabelSpacing + counterWidth + handbookWidth;
        minimumWidth = Math.Ceiling(new[]
        {
            tabsWidth, categoryWidth, optionsWidth, sortWidth, emptyActionWidth, craftableWidth, titleMinimumWidth, footerWidth
        }.Max());
        var textUtil = new TextDrawUtil();
        emptyTextHeight = Math.Ceiling(Enum.GetValues<RecipeBookEmptyState>()
            .Where(state => state != RecipeBookEmptyState.None)
            .Max(state => textUtil.GetMultilineTextHeight(textFont, Lang.Get(EmptyMessageKey(state)),
                (minimumWidth - viewportWidthReserve) * scale)) / scale);
        minimumHeight = Math.Ceiling(GuiStyle.TitleBarHeight + DialogPadding + tabsHeight + CategoryTitleTopPadding
            + categoryTitleHeight + RowSpacing + craftableHeight + RowSpacing + craftableHeight + RowSpacing + searchHeight + RowSpacing
            + Math.Max(GuiElementRecipeBookTiles.TileSize, emptyTextHeight + RowSpacing + footerHeight)
            + ListFooterSpacing + footerHeight + ContentInset);
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
        var savedPosition = SingleComposer == null ? capi.Gui.GetDialogPosition(ComposerName) : null;
        bool movable = SingleComposer?.GetTitleBar("titlebar").Movable ?? savedPosition != null;
        var next = movable
            ? RecipeBookLayout.PlaceMovable(SingleComposer?.Bounds
                ?? ElementBounds.Fixed(savedPosition!.X, savedPosition.Y, RecipeBookLayout.PreferredWidth, RecipeBookLayout.PreferredHeight),
                capi.Gui.WindowBounds, minimumWidth, minimumHeight)
            : RecipeBookLayout.Place(parentComposer.Bounds, grid, output, capi.Gui.WindowBounds,
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
            if (movable)
                capi.Gui.SetDialogPosition(ComposerName, new Vec2i((int)next.fixedX, (int)next.fixedY));
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
        SingleComposer.GetDynamicText("category-title").SetNewText(CategoryName(session.Category));
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
            .Select(action => SingleComposer.GetButton(action.Key).Bounds.fixedHeight + RowSpacing).DefaultIfEmpty(0).Max();
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
            button.Bounds.fixedY = empty.Bounds.fixedY + textHeight + RowSpacing;
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
