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
using static VanillaExpanded.RecipeBook.RecipeBookVisualMetrics;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiDialogRecipeBook : GuiDialog
{
    private const string ComposerName = "vanillaexpanded-recipebook";
    private const double ContentInset = OuterPadding + PanelPadding;
    private const double RowSpacing = 8;
    private static readonly string[] StandardCategories =
        ["construction", "decorative", "mechanics", "terrain", "tools", "clothing", "items"];
    private const double ScrollbarSpace = ScrollbarWidth + ScrollbarSpacing;
    private const double SearchTextPadding = 3;
    private const double TextWidthAllowance = 1;
    private const int HintMaxWidth = HintWidth;
    private readonly GuiDialog parent;
    private readonly GuiComposer parentComposer;
    private readonly RecipeBookSession session;
    private readonly CraftingTransferCoordinator coordinator;
    private readonly RecipeBookIconCache icons;
    private double composedScale;
    private int tileSize = (int)RecipeBookVisualMetrics.TileSize;
    private int composedTileSize;
    private double minimumWidth;
    private double minimumHeight;
    private double searchHeight;
    private double filterHeight;
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
    private double counterWidth;
    private readonly int maximumAlternatives;
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
        maximumAlternatives = catalogue.Groups.Select(group => group.Alternatives.Length).DefaultIfEmpty(1).Max();
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
        && ((SingleComposer?.GetTextInput("search")?.HasFocus ?? false)
            || options?.SizeInput.HasFocus == true);
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
        var oldTiles = SingleComposer?.GetElement("tiles") as GuiElementRecipeBookTiles;
        var view = oldTiles?.CaptureView();
        bool restoreOptions = options?.IsOpened == true;
        bool restoreSizeFocus = options?.SizeInput.HasFocus == true;
        string? sizeText = options?.SizeInput.GetText();
        bool rejectedSizeEdit = options?.RejectedSizeEdit == true;
        double width = bounds.fixedWidth;
        double height = bounds.fixedHeight;
        double contentTop = TitleHeight + TitleSpacing;
        var content = ElementBounds.Fixed(ContentInset, contentTop, width - 2 * ContentInset, height - contentTop - ContentInset);
        var tabsBounds = ElementBounds.Fixed(-PanelPadding, 0, content.fixedWidth + 2 * PanelPadding, tabsHeight);
        double panelTop = tabsHeight - Math.Max(1,
            GuiControlDrawing.Pixels(GuiControlMetrics.Outline, GuiElement.scaled(1))) / GuiElement.scaled(1);
        var categoryPanel = ElementBounds.Fixed(-PanelPadding, panelTop, content.fixedWidth + 2 * PanelPadding,
            content.fixedHeight - panelTop + PanelPadding);
        var categoryTitle = ElementBounds.Fixed(0, tabsHeight + PanelTopPadding, content.fixedWidth, categoryTitleHeight);
        double filterTop = categoryTitle.fixedY + categoryTitleHeight + HeadingSpacing;
        var craftableBounds = ElementBounds.Fixed(0, filterTop + (filterHeight - GuiControlMetrics.CheckboxSize) / 2,
            GuiControlMetrics.CheckboxSize, GuiControlMetrics.CheckboxSize);
        double textHeight = MainFont().GetFontExtents().Height / GuiElement.scaled(1);
        var craftableLabel = ElementBounds.Fixed(GuiControlMetrics.CheckboxSize + LabelSpacing,
            filterTop + (filterHeight - textHeight) / 2,
            content.fixedWidth - GuiControlMetrics.CheckboxSize - 2 * LabelSpacing - sortValueWidth, textHeight);
        var sortBounds = ElementBounds.Fixed(content.fixedWidth - sortValueWidth,
            filterTop + (filterHeight - GuiControlMetrics.NativeDropdownHeight) / 2,
            sortValueWidth, GuiControlMetrics.NativeDropdownHeight);
        var searchBounds = ElementBounds.Fixed(0, filterTop + filterHeight + FilterSpacing, content.fixedWidth, searchHeight);
        var footer = ElementBounds.Fixed(0, content.fixedHeight - footerHeight, content.fixedWidth, footerHeight);
        double listTop = searchBounds.fixedY + searchHeight + SearchSpacing;
        double listHeight = footer.fixedY - FooterSpacing - listTop;
        var listFrame = ElementBounds.Fixed(0, listTop, content.fixedWidth, listHeight);
        double border = ListBorderWidth;
        var viewport = ElementBounds.Fixed(border, listFrame.fixedY + border, content.fixedWidth - 2 * border - ScrollbarSpace,
            listFrame.fixedHeight - 2 * border);
        var scrollBounds = ElementBounds.Fixed(0, viewport.fixedY, ScrollbarWidth, viewport.fixedHeight)
            .FixedRightOf(viewport, ScrollbarSpacing);
        var previous = ElementBounds.Fixed(0, 0, previousWidth, footerHeight);
        var auto = ElementBounds.Fixed(previousWidth + FooterButtonSpacing, 0, autoWidth, footerHeight);
        var next = ElementBounds.Fixed(previousWidth + autoWidth + 2 * FooterButtonSpacing, 0, nextWidth, footerHeight);
        var handbook = ElementBounds.Fixed(content.fixedWidth - handbookWidth, 0, handbookWidth, footerHeight);
        var label = ElementBounds.Fixed((content.fixedWidth - counterWidth) / 2, 0, counterWidth, footerHeight);
        var tiles = new GuiElementRecipeBookTiles(capi, ElementBounds.Fixed(0, 0, viewport.fixedWidth, viewport.fixedHeight).WithParent(viewport),
            viewport, session, Inspect, Activate, icons, tileSize: tileSize);
        var newTooltip = new GuiElementRecipeBookTooltip(capi);
        var newOptions = new GuiElementRecipeBookOptions(capi, ElementBounds.Fixed(width - OptionsRightInset,
            OptionsIconTop, GuiElementRecipeBookOptions.IconSize, GuiElementRecipeBookOptions.IconSize));
        var composer = capi.Gui.CreateCompo(ComposerName, bounds);
        composer
            .AddInteractiveElement(new GuiElementRecipeBookBackground(capi, ElementBounds.Fill), "dialog-background")
            .AddInteractiveElement(new GuiElementRecipeBookTitleBar(capi, Lang.Get("vanillaexpanded:recipebook-title"),
                composer, () => CloseByUser(), ElementBounds.Fixed(0, 0, width, TitleHeight)), "titlebar")
            .BeginChildElements(content)
            .AddStaticElement(new GuiElementCustomDraw(capi, categoryPanel, GuiElementIconTab.DrawPanel), "category-panel")
            .AddInteractiveElement(new GuiElementClearableTextInput(capi, searchBounds,
                Lang.Get("vanillaexpanded:recipebook-search"), Lang.Get("vanillaexpanded:recipebook-clear-search"), text =>
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
            .AddInteractiveElement(new GuiElementStyledCheckbox(capi, enabled =>
            {
                tiles.ResetScroll();
                session.CraftableOnly.SetEnabled(enabled);
            }, craftableBounds, labelBounds: craftableLabel), "craftable-only")
            .AddStaticText(Lang.Get("vanillaexpanded:recipebook-craftable-only"), MainFont(),
                craftableLabel, "craftable-label")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-craftable-only-hint"), CairoFont.WhiteSmallText(),
                HintMaxWidth, ElementBounds.Fixed(0, filterTop, craftableLabel.fixedX + craftableLabel.fixedWidth, filterHeight),
                "craftable-hint")
            .AddInteractiveElement(new GuiElementStyledDropDown(capi, session.Sort.Orders.Select(order => order.Id).ToArray(),
                session.Sort.Orders.Select(order => Lang.Get(order.NameKey)).ToArray(),
                session.Sort.Orders.IndexOf(session.Sort.Selected), (id, _) =>
                {
                    if (session.Sort.Selected.Id == id) return;
                    tiles.ResetScroll();
                    session.Sort.Select(id);
                }, sortBounds), "sort")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-sort") + ": " + Lang.Get("vanillaexpanded:recipebook-sort-hint"),
                CairoFont.WhiteSmallText(), HintMaxWidth, sortBounds.FlatCopy(),
                "sort-hint")
            .AddStaticElement(new GuiElementCustomDraw(capi, listFrame, GuiElementRecipeBookBackground.DrawList), "recipes-background")
            .BeginClip(viewport)
                .AddInteractiveElement(tiles, "tiles")
            .EndClip()
            .AddInteractiveElement(new GuiElementStyledScrollbar(capi, tiles.Scroll, scrollBounds,
                tiles.RowPitch), "scroll")
            .AddDynamicText("", MainFont().WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(viewport.fixedX, viewport.fixedY, viewport.fixedWidth, emptyTextHeight), "empty")
            .AddInteractiveElement(StyledButton(Lang.Get("vanillaexpanded:recipebook-clear-search"), ClearSearch,
                ElementBounds.Fixed(viewport.fixedX, viewport.fixedY + emptyTextHeight + RowSpacing, clearSearchWidth, footerHeight)), "clear-search")
            .AddInteractiveElement(StyledButton(Lang.Get("vanillaexpanded:recipebook-show-uncraftable"), ShowUncraftable,
                ElementBounds.Fixed(viewport.fixedX, viewport.fixedY + emptyTextHeight + RowSpacing, showUncraftableWidth, footerHeight)), "show-uncraftable")
            .BeginChildElements(footer)
            .AddInteractiveElement(StyledButton("<", () => Cycle(-1), previous), "previous")
            .AddInteractiveElement(StyledButton(Lang.Get("vanillaexpanded:recipebook-auto"),
                () => { session.Auto(); return true; }, auto), "auto")
            .AddInteractiveElement(StyledButton(">", () => Cycle(1), next), "next")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-alternative-hint"), CairoFont.WhiteSmallText(),
                HintMaxWidth, ElementBounds.Fixed(0, 0, next.fixedX + nextWidth, footerHeight))
            .AddDynamicText("", FooterFont().WithOrientation(EnumTextOrientation.Center), label, "alternative")
            .AddInteractiveElement(StyledButton(Lang.Get("vanillaexpanded:recipebook-handbook"), OpenHandbook, handbook), "handbook")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-handbook-hint"), CairoFont.WhiteSmallText(),
                HintMaxWidth, ElementBounds.Fixed(handbook.fixedX, 0, handbookWidth, footerHeight), "handbook-hint")
            .EndChildElements()
            .AddInteractiveElement(newTooltip, "tooltip")
            .EndChildElements()
            .AddInteractiveElement(newOptions, "options")
            .AddHoverText(Lang.Get("vanillaexpanded:recipebook-options"), CairoFont.WhiteSmallText(),
                HintMaxWidth, newOptions.Bounds.FlatCopy(), "options-hint")
            .Compose(focusFirstElement);
        SingleComposer?.Dispose();
        SingleComposer = composer;
        tooltip = newTooltip;
        options = newOptions;
        if (restoreOptions)
        {
            composer.FocusElement(newOptions.TabIndex);
            newOptions.RestoreMenu(restoreSizeFocus, sizeText, rejectedSizeEdit);
        }
        composer.GetSwitch("craftable-only").SetValue(session.CraftableOnly.Enabled);
        ((GuiElementIconTabs)composer.GetElement("categories")).SetValue(Array.IndexOf(categories, session.Category), false);
        composer.GetTextInput("search").SetValue(search);
        tiles.Bind(composer.GetScrollbar("scroll"));
        if (view != null)
        {
            if (view.HasFocus && focusFirstElement) composer.FocusElement(tiles.TabIndex);
            tiles.RestoreView(view with { HasFocus = view.HasFocus && focusFirstElement });
        }
        UpdateLabels();
    }

    private static string CategoryName(string? code) => code switch
    {
        null => Lang.Get("vanillaexpanded:recipebook-category-all"),
        CraftingCatalogue.OtherCategory => Lang.Get("vanillaexpanded:recipebook-category-other"),
        _ => Lang.GetIfExists("tabname-" + code) ?? code
    };

    private static double NativeTitlebarControlsWidth =>
        2 * GuiElementDialogTitleBar.unscaledCloseIconSize + NativeMenuExpansion + NativeTitleRightSpacing;

    private static double OptionsRightInset =>
        NativeTitlebarControlsWidth + OptionsIconSpacing + GuiElementRecipeBookOptions.IconSize;

    private static double ListBorderWidth => Math.Max(1,
        GuiControlDrawing.Pixels(ListOutline, GuiElement.scaled(1))) / GuiElement.scaled(1);

    private static CairoFont CategoryTitleFont() => MainFont().WithFontSize(HeadingFontSize)
        .WithColor(GuiControlPalette.Color(GuiControlPalette.Heading));

    private static CairoFont MainFont() => CairoFont.WhiteSmallText().WithFontSize(GuiControlMetrics.TextFontSize)
        .WithColor(GuiControlPalette.Color(GuiControlPalette.Text));

    private static CairoFont FooterFont() => CairoFont.WhiteSmallText().WithFontSize(GuiControlMetrics.CompactFontSize)
        .WithColor(GuiControlPalette.Color(GuiControlPalette.ButtonText));

    // Native button bounds measure the content box; layout reserves its padded outer width.
    private GuiElementStyledButton StyledButton(string text, ActionConsumable clicked, ElementBounds bounds) =>
        new(capi, text, clicked, bounds.WithFixedWidth(bounds.fixedWidth - 2 * GuiControlMetrics.TextInset), FooterFont());

    private void MeasureLayout()
    {
        double scale = GuiElement.scaled(1);
        var buttonFont = FooterFont();
        var textFont = MainFont();
        var sortFont = FooterFont();
        var titleFont = MainFont().WithWeight(Cairo.FontWeight.Bold);
        var categoryFont = CategoryTitleFont();
        double ButtonWidth(string text) => Math.Ceiling(buttonFont.GetTextExtents(text).Width / scale + TextWidthAllowance)
            + 2 * GuiControlMetrics.TextInset;
        previousWidth = Math.Max(NavigationWidth, ButtonWidth("<"));
        autoWidth = Math.Max(AutoWidth, ButtonWidth(Lang.Get("vanillaexpanded:recipebook-auto")));
        nextWidth = Math.Max(NavigationWidth, ButtonWidth(">"));
        handbookWidth = Math.Max(HandbookWidth, ButtonWidth(Lang.Get("vanillaexpanded:recipebook-handbook")));
        clearSearchWidth = ButtonWidth(Lang.Get("vanillaexpanded:recipebook-clear-search"));
        showUncraftableWidth = ButtonWidth(Lang.Get("vanillaexpanded:recipebook-show-uncraftable"));
        footerHeight = Math.Max(FooterHeight, Math.Ceiling(Math.Max(buttonFont.GetFontExtents().Height,
            textFont.GetFontExtents().Height) / scale));
        searchHeight = Math.Max(GuiControlMetrics.SearchHeight,
            Math.Ceiling(textFont.GetFontExtents().Height / scale + 2 * SearchTextPadding));
        filterHeight = Math.Max(GuiControlMetrics.NativeDropdownHeight, Math.Ceiling(textFont.GetFontExtents().Height / scale));
        tabsHeight = GuiElementIconTabs.TabHeight;
        categoryTitleHeight = Math.Max(HeadingHeight, Math.Ceiling(categoryFont.GetFontExtents().Height / scale));
        char widestDigit = Enumerable.Range('0', 10).Select(value => (char)value)
            .MaxBy(value => buttonFont.GetTextExtents(value.ToString()).Width);
        string digits = new(widestDigit, maximumAlternatives.ToString().Length);
        counterWidth = Math.Ceiling(buttonFont.GetTextExtents(digits + "/" + digits).Width / scale + TextWidthAllowance);
        double titleWidth = titleFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-title")).Width / scale;
        double filterWidth = 2 * ContentInset + GuiControlMetrics.CheckboxSize + 2 * LabelSpacing
            + textFont.GetTextExtents(Lang.Get("vanillaexpanded:recipebook-craftable-only")).Width / scale + TextWidthAllowance;
        sortValueWidth = GuiElementStyledDropDown.MeasureWidth(
            session.Sort.Orders.Select(order => Lang.Get(order.NameKey)).ToArray(), sortFont);
        filterWidth += sortValueWidth;
        double optionsWidth = OuterPadding + OptionsRightInset - GuiElementRecipeBookOptions.IconSize
            + GuiElementRecipeBookOptions.MeasurePopupWidth(textFont, Lang.Get("vanillaexpanded:recipebook-remember-open"));
        double tabsWidth = 2 * OuterPadding + GuiElementIconTabs.MeasureWidth(categories.Length);
        double categoryWidth = 2 * ContentInset
            + categories.Max(code => categoryFont.GetTextExtents(CategoryName(code)).Width / scale + TextWidthAllowance);
        double viewportWidthReserve = 2 * ContentInset + 2 * ListBorderWidth + ScrollbarSpace;
        double emptyActionWidth = viewportWidthReserve + Math.Max(clearSearchWidth, showUncraftableWidth);
        double titleMinimumWidth = GuiStyle.ElementToDialogPadding + titleWidth + LabelSpacing + OptionsRightInset;
        double navigationWidth = previousWidth + autoWidth + nextWidth + 2 * FooterButtonSpacing;
        double footerWidth = 2 * ContentInset + 2 * (Math.Max(navigationWidth, handbookWidth) + FooterLabelSpacing) + counterWidth;
        minimumWidth = Math.Ceiling(new[]
        {
            tabsWidth, categoryWidth, optionsWidth, filterWidth, emptyActionWidth, titleMinimumWidth, footerWidth,
            viewportWidthReserve + tileSize + 2 * RecipeBookTileGeometry.HorizontalPadding
        }.Max());
        var textUtil = new TextDrawUtil();
        emptyTextHeight = Math.Ceiling(Enum.GetValues<RecipeBookEmptyState>()
            .Where(state => state != RecipeBookEmptyState.None)
            .Max(state => textUtil.GetMultilineTextHeight(textFont, Lang.Get(EmptyMessageKey(state)),
                (minimumWidth - viewportWidthReserve) * scale)) / scale);
        minimumHeight = Math.Ceiling(TitleHeight + TitleSpacing + tabsHeight + PanelTopPadding
            + categoryTitleHeight + HeadingSpacing + filterHeight + FilterSpacing + searchHeight + SearchSpacing
            + 2 * ListBorderWidth + Math.Max(RecipeBookTileGeometry.TopPadding + tileSize, emptyTextHeight + RowSpacing + footerHeight)
            + FooterSpacing + footerHeight + ContentInset);
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
        tileSize = RecipeBookSystem.GetTileSize(capi);
        if (SingleComposer == null || composedScale != RuntimeEnv.GUIScale || composedTileSize != tileSize) MeasureLayout();
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
            || composedTileSize != tileSize
            || SingleComposer.Bounds.fixedWidth != next.fixedWidth || SingleComposer.Bounds.fixedHeight != next.fixedHeight)
        {
            if (movable)
                capi.Gui.SetDialogPosition(ComposerName, new Vec2i((int)next.fixedX, (int)next.fixedY));
            Compose(next, focusFirstElement);
            composedScale = RuntimeEnv.GUIScale;
            composedTileSize = tileSize;
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

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        options?.OnMouseWheel(capi, args);
        if (!args.IsHandled) base.OnMouseWheel(args);
    }

    public override void OnKeyDown(KeyEvent args)
    {
        if (args.Handled) return;
        if (options?.IsOpened == true && args.KeyCode == (int)GlKeys.Tab)
        {
            options.OnKeyDown(capi, args);
            if (args.Handled) return;
            int direction = args.ShiftPressed ? -1 : 1;
            int count = SingleComposer.MaxTabIndex + 1;
            for (int offset = 1; offset <= count; offset++)
            {
                int index = (options.TabIndex + direction * offset + count) % count;
                if (!SingleComposer.FocusElement(index)) continue;
                args.Handled = true;
                return;
            }
        }
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
            button.Bounds.fixedX = viewport.fixedX + (viewport.fixedWidth - button.Bounds.OuterWidth / scale) / 2;
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
