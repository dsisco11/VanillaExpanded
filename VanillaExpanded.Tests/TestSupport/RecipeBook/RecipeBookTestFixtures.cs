// Shared Recipe Book dialog/session fixtures retained for the focused suites planned in Phase 3.
using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;
using VanillaExpanded.Tests.Unit.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal sealed class MenuFixture : IDisposable
{
    private readonly RecipeBookRenderFixture graphics;
    internal readonly LifecycleFixture Lifecycle;
    internal readonly ParentDialog Parent;
    private readonly List<GuiDialog> loaded = new();
    private readonly List<GuiComposer> composers = new();
    private readonly string originalLocale = Lang.CurrentLocale;
    private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
    internal IEnumerable<GuiDialogRecipeBook> Menus => loaded.OfType<GuiDialogRecipeBook>();
    internal GuiElementRecipeBookButton ToggleButton =>
        (GuiElementRecipeBookButton)Parent.Composers["maininventory"].GetToggleButton("vanillaexpanded-crafting-toggle");
    internal readonly IAsset BookAsset;
    internal readonly IAsset OptionsAsset;
    internal readonly List<(int Width, int Height, int SurfaceWidth, int SurfaceHeight, int? Color)> OptionsDraws = new();
    internal readonly List<(IAsset Asset, int Width, int Height, int? Color)> BookDraws = new();
    internal readonly Dictionary<string, IAsset> CategoryAssets = new();
    internal readonly List<(IAsset Asset, int X, int Y, int Width, int Height, int SurfaceWidth, int SurfaceHeight, int? Color)> CategoryDraws = new();
    internal int TextureCount => graphics.Textures.Count;
    internal readonly Dictionary<string, Vec2i> DialogPositions;

    internal static string GetAssetPath(params string[] assetSegments)
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "VanillaExpanded", "assets", "vanillaexpanded",
            System.IO.Path.Combine(assetSegments)));
        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException(
                $"Required Recipe Book test asset '{string.Join("/", assetSegments)}' was not found.", path);
        return path;
    }

    internal MenuFixture(bool enabled = true, float scale = 1, bool longLabels = false, bool includeRecipes = false,
        Dictionary<string, bool>? savedSettings = null,
        Dictionary<string, Vec2i>? savedPositions = null, Dictionary<string, string>? categoryNames = null)
    {
        graphics = new RecipeBookRenderFixture(scale);
        DialogPositions = savedPositions ?? new();
        graphics.Gui.Setup(value => value.GetDialogPosition(It.IsAny<string>()))
            .Returns((string name) => DialogPositions.GetValueOrDefault(name));
        graphics.Gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()))
            .Callback<string, Vec2i?>((name, position) =>
            {
                if (position == null) DialogPositions.Remove(name);
                else DialogPositions[name] = new(position.X, position.Y);
            });
        Lifecycle = new LifecycleFixture(enabled, savedSettings);
        var translations = new Mock<ITranslationService>();
        translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => key switch
            {
                "vanillaexpanded:recipebook-title" => longLabels ? "Recipe book with all available recipes" : "Recipe Book",
                "vanillaexpanded:recipebook-auto" => longLabels ? "Automatic selection" : "Auto",
                "vanillaexpanded:recipebook-handbook" => longLabels ? "Open survival handbook" : "Handbook",
                "vanillaexpanded:recipebook-craftable-only" => longLabels
                    ? "Hide recipes without all required ingredients" : "Hide uncraftable",
                "vanillaexpanded:recipebook-sort" => longLabels ? "Recipe sorting preference" : "Sort by",
                "vanillaexpanded:recipebook-sort-availability" => longLabels ? "Current ingredient availability" : "Availability",
                "vanillaexpanded:recipebook-sort-name" => longLabels ? "Alphabetical output name" : "Name",
                "vanillaexpanded:recipebook-empty" => "No recipes are available in this category.",
                "vanillaexpanded:recipebook-empty-search" => "No recipes match your search in this category.",
                "vanillaexpanded:recipebook-empty-craftable" => "No recipes in this category are currently craftable.",
                "vanillaexpanded:recipebook-empty-craftable-search" => longLabels
                    ? "Recipes match the current search in the selected category, but none can currently be crafted with the available ingredients."
                    : "Recipes match your search, but none are currently craftable.",
                "vanillaexpanded:recipebook-clear-search" => longLabels ? "Clear the current search text" : "Clear search",
                "vanillaexpanded:recipebook-show-uncraftable" => longLabels
                    ? "Show recipes with unavailable ingredients" : "Show uncraftable",
                "vanillaexpanded:recipebook-toggle" => "Recipe Book",
                "vanillaexpanded:recipebook-remember-open" => longLabels
                    ? "Automatically reopen the recipe book with the inventory" : "Reopen with inventory",
                "vanillaexpanded:recipebook-category-all" => longLabels
                    ? "All recipes available in the selected crafting catalogue" : "All",
                "vanillaexpanded:recipebook-category-other" => "Other",
                _ => key
            });
        translations.Setup<string?>(value => value.GetIfExists(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => categoryNames?.GetValueOrDefault(key));
        Lang.AvailableLanguages["en"] = translations.Object;
        Lang.ChangeLanguage("en");
        var api = Lifecycle.Api;
        api.SetupGet(value => value.Gui).Returns(graphics.Gui.Object);
        api.SetupGet(value => value.Render).Returns(graphics.Render.Object);
        api.SetupGet(value => value.Input).Returns(graphics.Api.Object.Input);
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        api.SetupGet(value => value.Network).Returns(Mock.Of<IClientNetworkAPI>());
        var registry = new Mock<IClassRegistryAPI>();
        registry.Setup(value => value.CreateInvNetworkUtil(It.IsAny<InventoryBase>(), api.Object))
            .Returns(Mock.Of<IInventoryNetworkUtil>());
        api.SetupGet(value => value.ClassRegistry).Returns(registry.Object);
        var assets = new Mock<IAssetManager>();
        assets.Setup(value => value.Get(It.IsAny<AssetLocation>()))
            .Returns(Mock.Of<IAsset>(value => value.Data == Array.Empty<byte>()));
        byte[] svg = File.ReadAllBytes(GetAssetPath("textures", "gui", "recipe-book.svg"));
        BookAsset = Mock.Of<IAsset>(value => value.Data == svg);
        assets.Setup(value => value.Get(new AssetLocation("vanillaexpanded:textures/gui/recipe-book.svg")))
            .Returns(BookAsset);
        byte[] optionsSvg = File.ReadAllBytes(GetAssetPath("textures", "gui", "recipe-book-options.svg"));
        OptionsAsset = Mock.Of<IAsset>(value => value.Data == optionsSvg);
        assets.Setup(value => value.Get(new AssetLocation("vanillaexpanded:textures/gui/recipe-book-options.svg")))
            .Returns(OptionsAsset);
        foreach (string code in RecipeBookCategoryIcons.Codes)
        {
            byte[] categorySvg = File.ReadAllBytes(GetAssetPath("textures", "gui", "recipe-book-categories", code + ".svg"));
            var asset = Mock.Of<IAsset>(value => value.Data == categorySvg);
            CategoryAssets.Add(code, asset);
            assets.Setup(value => value.Get(RecipeBookCategoryIcons.Asset(code))).Returns(asset);
        }
        api.SetupGet(value => value.Assets).Returns(assets.Object);
        graphics.Gui.Setup(value => value.DrawSvg(It.IsAny<IAsset>(), It.IsAny<ImageSurface>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Callback<IAsset, ImageSurface, int, int, int, int, int?>((asset, surface, x, y, width, height, color) =>
            {
                if (ReferenceEquals(asset, BookAsset)) BookDraws.Add((asset, width, height, color));
                else if (ReferenceEquals(asset, OptionsAsset))
                    OptionsDraws.Add((width, height, surface.Width, surface.Height, color));
                else CategoryDraws.Add((asset, x, y, width, height, surface.Width, surface.Height, color));
            });
        graphics.Render.Setup(value => value.BitmapCreateFromPng(It.IsAny<byte[]>()))
            .Returns(() => new BitmapExternal(2, 2));
        graphics.Gui.SetupGet(value => value.Icons).Returns(new IconUtil(api.Object));
        graphics.Gui.SetupGet(value => value.LoadedGuis).Returns(loaded);
        graphics.Gui.Setup(value => value.RegisterDialog(It.IsAny<GuiDialog[]>()))
            .Callback<GuiDialog[]>(dialogs => loaded.AddRange(dialogs));
        graphics.Gui.Setup(value => value.RequestFocus(It.IsAny<GuiDialog>()))
            .Callback<GuiDialog>(dialog =>
            {
                foreach (var existing in loaded) existing.UnFocus();
                dialog.Focus();
            });
        var window = ElementBounds.Fixed(0, 0, 1920, 1080).WithEmptyParent();
        window.CalcWorldBounds();
        graphics.Gui.SetupGet(value => value.WindowBounds).Returns(window);
        graphics.Render.SetupGet(value => value.FrameWidth).Returns(() => window.OuterWidthInt);
        graphics.Render.SetupGet(value => value.FrameHeight).Returns(() => window.OuterHeightInt);
        graphics.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => CreateComposer(name, bounds));
        Lifecycle.World.SetupGet(value => value.Player).Returns(Lifecycle.F.Player);
        Lifecycle.World.SetupGet(value => value.Rand).Returns(new Random(123));
        var manager = (ClientPlayerInventoryManager)Lifecycle.F.Player.InventoryManager;
        manager.Inventories.Clear();
        var grid = new InventoryCraftingGrid("craftinggrid-test", null!) { Api = Lifecycle.F.Api.Object };
        grid.InvNetworkUtil.Api = Lifecycle.F.Api.Object;
        grid.Open(Lifecycle.F.Player);
        manager.Inventories.Add(grid.InventoryID, grid);
        if (!includeRecipes) Lifecycle.Recipes.Clear();
        Lifecycle.FinalizeWorld();
        Parent = new ParentDialog(api.Object);
        Recompose();
        Lifecycle.System.Attach(Parent, Parent.Composers["maininventory"]);
    }

    private GuiComposer CreateComposer(string name, ElementBounds bounds) =>
        (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { Lifecycle.Api.Object, bounds, name }, null)!;

    internal void Recompose()
    {
        foreach (var recipe in Lifecycle.Recipes)
            if (recipe.Output?.ResolvedItemStack?.Collectible is MockItem item)
                item.SetApi(Lifecycle.Api.Object);
        var composer = CreateComposer("inventory", ElementBounds.Fixed(500, 100, 400, 500).WithEmptyParent());
        composers.Add(composer);
        composer.AddStaticElement(new GuiElementParent(Lifecycle.Api.Object,
            ElementBounds.Fixed(20, 50, 144, 144)), "craftinggrid");
        composer.AddStaticElement(new GuiElementParent(Lifecycle.Api.Object,
            ElementBounds.Fixed(72, 214, 48, 48)), "outputslot");
        Lifecycle.System.Attach(Parent, composer);
        Parent.Composers["maininventory"] = composer;
    }

    internal void Toggle()
    {
        var button = ToggleButton.Bounds;
        int x = (int)(button.absX + 5);
        int y = (int)(button.absY + 5);
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        Parent.OnMouseDown(down);
        Assert.True(down.Handled);
        Parent.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
    }

    internal void SelectMovement(GuiDialogRecipeBook book, bool movable)
    {
        var titlebar = book.SingleComposer.GetTitleBar("titlebar");
        int x = (int)(titlebar.Bounds.absX + titlebar.Bounds.OuterWidth - GuiElement.scaled(44));
        int y = (int)(titlebar.Bounds.absY + GuiElement.scaled(10));
        SetMouse(x, y);
        book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        var menu = (GuiElementListMenu)typeof(GuiElementDialogTitleBar)
            .GetField("listMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(titlebar)!;
        Assert.True(menu.IsOpened);
        Assert.Equal(new[] { "auto", "manual" }, menu.Values);
        x = (int)(menu.Bounds.renderX + GuiElement.scaled(5));
        y = (int)(menu.Bounds.renderY + menu.Bounds.InnerHeight + GuiElement.scaled(movable ? 45 : 15));
        SetMouse(x, y);
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        book.OnMouseDown(down);
        Assert.True(down.Handled);
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.False(menu.IsOpened);
        Assert.Equal(movable, titlebar.Movable);
    }

    internal GuiElementRecipeBookOptions OpenOptions(GuiDialogRecipeBook book)
    {
        var options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        int loadedCount = loaded.Count;
        int x = (int)(options.Bounds.absX + options.Bounds.OuterWidth / 2);
        int y = (int)(options.Bounds.absY + options.Bounds.OuterHeight / 2);
        book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.True(options.IsOpened);
        Assert.Equal(loadedCount, loaded.Count);
        return options;
    }

    internal void ToggleReopenOption(GuiDialogRecipeBook book)
    {
        var options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        int x = (int)(options.PopupBounds.absX + GuiElement.scaled(10));
        int y = (int)(options.PopupBounds.absY + GuiElement.scaled(15));
        var input = Mock.Get(Lifecycle.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns(x);
        input.SetupGet(value => value.MouseY).Returns(y);
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        book.OnMouseDown(down);
        Assert.True(down.Handled);
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
    }

    internal void ToggleCraftable(GuiDialogRecipeBook menu, bool label = false)
    {
        var bounds = label ? menu.SingleComposer.GetElement("craftable-label").Bounds
            : menu.SingleComposer.GetSwitch("craftable-only").Bounds;
        int x = (int)(bounds.absX + 5);
        int y = (int)(bounds.absY + 5);
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        menu.OnMouseDown(down);
        Assert.True(down.Handled);
        menu.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
    }

    internal void Click(GuiElement button, bool handled = true)
    {
        var args = new MouseEvent((int)(button.Bounds.absX + 5), (int)(button.Bounds.absY + 5),
            EnumMouseButton.Left, 0);
        button.OnMouseDownOnElement(Lifecycle.Api.Object, args);
        button.OnMouseUpOnElement(Lifecycle.Api.Object, args);
        Assert.Equal(handled, args.Handled);
    }

    internal void SetMouse(int x, int y)
    {
        var input = Mock.Get(Lifecycle.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns(x);
        input.SetupGet(value => value.MouseY).Returns(y);
    }

    internal void AssertToggleHidden()
    {
        Assert.False(ToggleButton.Enabled);
        AssertOpenerInvisible();
        Assert.Empty(Menus);
    }

    internal void AssertOpenerInvisible()
    {
        Assert.False(ToggleButton.Visible);
        Assert.False(ToggleButton.Focusable);
        graphics.Render.Invocations.Clear();
        ToggleButton.RenderInteractiveElements(0);
        Assert.Empty(graphics.Render.Invocations);
        var mouse = new MouseEvent((int)ToggleButton.Bounds.absX + 5, (int)ToggleButton.Bounds.absY + 5,
            EnumMouseButton.Left, 0);
        ToggleButton.OnMouseDownOnElement(Lifecycle.Api.Object, mouse);
        Assert.False(mouse.Handled);
        Parent.OnMouseDown(mouse);
        Assert.Empty(Menus);
        ToggleButton.OnFocusGained();
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        ToggleButton.OnKeyDown(Lifecycle.Api.Object, enter);
        Assert.False(enter.Handled);
        var hint = ToggleButton.Hint;
        var input = Mock.Get(Lifecycle.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns(mouse.X);
        input.SetupGet(value => value.MouseY).Returns(mouse.Y);
        hint.RenderInteractiveElements(0);
        Assert.False(hint.IsVisible);
        Assert.False(hint.IsNowShown);
    }

    internal void AssertToggleOpen()
    {
        Assert.True(ToggleButton.Visible);
        Assert.True(ToggleButton.Enabled);
        Assert.True(ToggleButton.Focusable);
        Assert.True(ToggleButton.On);
        var bounds = ToggleButton.Bounds;
        Assert.True(ToggleButton.IsPositionInside((int)(bounds.absX + 5), (int)(bounds.absY + 5)));
        graphics.Render.Invocations.Clear();
        ToggleButton.RenderInteractiveElements(0);
        Assert.NotEmpty(graphics.Render.Invocations);
    }

    internal void ResizePanelSpace(double width, double height)
    {
        var window = graphics.Gui.Object.WindowBounds;
        window.fixedWidth = 900 + 2 / GuiElement.scaled(1) + width;
        window.fixedHeight = height;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        var parent = Parent.Composers["maininventory"].Bounds;
        parent.fixedY = 0;
        parent.fixedHeight = height;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
    }

    internal void SetEnabled(bool enabled)
    {
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(VanillaExpandedModSystem.Config,
            new Vintagestory.API.Datastructures.StringAttribute(
                "{\"EnableRecipeBook\":\"" + (enabled ? "true" : "false") + "\"}")));
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.Systems).Returns(Lifecycle.Loaded
            ? new ModSystem[] { Lifecycle.System } : Array.Empty<ModSystem>());
        Lifecycle.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        LiveConfigReload.NotifyAll(Lifecycle.Api.Object);
    }

    internal void ClickCloseButton(GuiDialogRecipeBook menu)
    {
        var titlebar = menu.SingleComposer.GetTitleBar("titlebar");
        var args = new MouseEvent((int)(titlebar.Bounds.absX + titlebar.Bounds.OuterWidth
            - GuiElement.scaled(12 + GuiElementDialogTitleBar.unscaledCloseIconSize / 2.0)),
            (int)(titlebar.Bounds.absY + GuiElement.scaled(10)), EnumMouseButton.Left, 0);
        titlebar.OnMouseUpOnElement(Lifecycle.Api.Object, args);
        Assert.True(args.Handled);
    }

    public void Dispose()
    {
        Lifecycle.Dispose();
        Parent.Dispose();
        foreach (var composer in composers.Take(composers.Count - 1)) composer.Dispose();
        graphics.Dispose();
        if (originalEnglish == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = originalEnglish;
        Lang.ChangeLanguage(originalLocale);
    }
}

internal sealed class ParentDialog(ICoreClientAPI api) : GuiDialog(api)
{
    internal Action? Opening;
    public override string ToggleKeyCombinationCode => "inventory";
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Opening?.Invoke();
    }
}

internal sealed class NativeScope : IDisposable
{
    private readonly int thread = RuntimeEnv.MainThreadId;
    private readonly float scale = RuntimeEnv.GUIScale;
    internal NativeScope()
    {
        RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;
        RuntimeEnv.GUIScale = 1;
    }
    public void Dispose()
    {
        RuntimeEnv.MainThreadId = thread;
        RuntimeEnv.GUIScale = scale;
    }
}

internal sealed class LifecycleFixture : IDisposable
{
    internal readonly AllocationFixture F = new();
    internal readonly Mock<ICoreClientAPI> Api = new();
    internal readonly Mock<IClientWorldAccessor> World = new();
    internal readonly Mock<IClientEventAPI> Events = new();
    internal readonly Queue<Action> Queue = new();
    internal readonly List<GridRecipe> Recipes = new();
    internal readonly RecipeBookSystem System;
    internal readonly bool Loaded;
    internal int Builds;
    private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableRecipeBook;

    internal LifecycleFixture(bool enabled = true, Dictionary<string, bool>? savedSettings = null)
    {
        savedSettings ??= new();
        var boolSettings = new Mock<ISettingsClass<bool>>();
        boolSettings.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string key, bool defaultValue) => savedSettings.GetValueOrDefault(key, defaultValue));
        boolSettings.SetupSet(value => value[It.IsAny<string>()] = It.IsAny<bool>())
            .Callback((string key, bool value) => savedSettings[key] = value);
        Api.SetupGet(value => value.Settings).Returns(Mock.Of<ISettings>(value => value.Bool == boolSettings.Object));
        VanillaExpandedModSystem.Config.EnableRecipeBook = enabled;
        Recipes.Add(F.Recipe(F.Ingredient(F.Stack(1, "a", 1))));
        F.Register(Recipes.ToArray());
        Api.SetupGet(value => value.World).Returns(World.Object);
        Api.SetupGet(value => value.ModLoader).Returns(Mock.Of<IModLoader>());
        Api.SetupGet(value => value.Event).Returns(Events.Object);
        World.SetupGet(value => value.GridRecipes).Returns(Recipes);
        World.SetupGet(value => value.FastSearchRecipesByIngredient).Returns(F.Catalogue.Index);
        Events.Setup(value => value.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => Queue.Enqueue(action));
        System = new RecipeBookSystem(_ =>
        {
            Builds++;
            return (F.Catalogue.Build(Recipes.ToArray()), F.Allocator);
        });
        Loaded = System.ShouldLoad(EnumAppSide.Client);
        if (Loaded) System.StartClientSide(Api.Object);
    }

    internal void FinalizeWorld() => Events.Raise(value => value.LevelFinalize += null);
    internal void Drain() { while (Queue.TryDequeue(out var action)) action(); }

    public void Dispose()
    {
        System.Dispose();
        VanillaExpandedModSystem.Config.EnableRecipeBook = originalEnabled;
    }
}
