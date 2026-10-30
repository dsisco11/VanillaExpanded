using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.CraftingMenu;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
[Collection("Crafting native GUI")]
public sealed class CraftingMenuInteractionTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 20)]
    public void SessionUsesRealAllocationAndCoordinatorForBothInputs(bool shift, int expected)
    {
        using var f = new TransferFixture(2, 20);
        var catalogue = f.F.Catalogue.Build(f.Entry.Recipe);
        var session = new CraftingMenuSession(catalogue, f.F.Allocator,
            () => CraftingInventorySnapshot.Capture(f.F.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            f.Coordinator.Execute);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.True(row.Evaluation.CanCraft);
        Assert.Equal(2, Assert.Single(row.Allocation!.Ordinary).Quantity);
        var result = session.Activate(row.Output.Id, shift);
        Assert.Equal(CraftingDispatchStatus.Dispatched, result.Status);
        Assert.Equal(expected, f.F.Grid[0].StackSize);
        Assert.Single(f.Packets);
        session.Refresh();
        Assert.Equal(2, Assert.Single(session.Rows).Evaluation.Allocated);
        Assert.True(f.F.Grid[9].Empty);
    }

    [Fact]
    public void DetectionCombinesGridAndCarriedItemsAndRefreshesWhenGridInputsChange()
    {
        using var f = new TransferFixture(6, 1);
        f.F.Backpack[4].Itemstack = f.Ingredient(2);
        f.F.Grid[8].Itemstack = f.Ingredient(3);
        f.F.Grid[9].Itemstack = f.Ingredient(64);
        var session = new CraftingMenuSession(f.F.Catalogue.Build(f.Entry.Recipe), f.F.Allocator,
            f.Snapshot, f.Coordinator.Execute);
        f.Coordinator.InventoryChanged += session.Refresh;
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.True(row.Evaluation.CanCraft);
        Assert.Equal(6, row.Evaluation.Allocated);
        var sources = Assert.Single(row.Allocation!.Ordinary).Sources;
        Assert.Equal(new[] { f.F.Hotbar.InventoryID, f.F.Backpack.InventoryID, f.F.Grid.InventoryID },
            sources.Select(source => source.Slot.InventoryId));
        Assert.Equal(new[] { 1, 2, 3 }, sources.Select(source => source.Quantity));

        f.F.Hotbar[0].Itemstack = null;
        f.F.Grid[8].Itemstack = f.Ingredient(4);
        f.F.Hotbar.DidModifyItemSlot(f.F.Hotbar[0]);
        f.F.Grid.DidModifyItemSlot(f.F.Grid[8]);
        Assert.Single(f.RefreshQueue);
        f.Refresh();
        Assert.True(Assert.Single(session.Rows).Evaluation.CanCraft);
        Assert.Equal(6, Assert.Single(session.Rows).Evaluation.Allocated);

        f.F.Grid[8].Itemstack = f.Ingredient(3);
        f.F.Grid.DidModifyItemSlot(f.F.Grid[8]);
        f.Refresh();
        row = Assert.Single(session.Rows);
        Assert.False(row.Evaluation.CanCraft);
        Assert.Equal(5, row.Evaluation.Allocated);
        Assert.Equal(1, Assert.Single(row.Evaluation.Missing).Quantity);
        Assert.Equal(64, f.F.Grid[9].StackSize);
        Assert.Empty(f.Packets);
    }

    [Fact]
    public void DeferredOrderingKeepsShownRecipeButUpdatesItsDeficitsAndRevalidatesClick()
    {
        using var f = new TransferFixture(2, 2);
        var a = f.Entry.Recipe;
        var other = f.F.Stack(2, "other", 2);
        var b = f.F.Recipe(f.F.Ingredient(other, 2));
        f.F.Register(a, b);
        var catalogue = f.F.Catalogue.Build(a, b);
        var session = new CraftingMenuSession(catalogue, f.F.Allocator,
            () => CraftingInventorySnapshot.Capture(f.F.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            f.Coordinator.Execute);
        session.Refresh();
        int first = session.Rows[0].Output.Id;
        session.Interact(first);
        f.F.Hotbar[0].Itemstack = other;
        session.Refresh();
        Assert.Equal(first, session.Rows[0].Output.Id);
        Assert.False(session.Rows[0].Evaluation.CanCraft);
        Assert.Equal(2, Assert.Single(session.Rows[0].Evaluation.Missing).Quantity);
        var result = session.Activate(first, false);
        Assert.Equal(CraftingDispatchStatus.Rejected, result.Status);
        Assert.Equal(CraftingTransferPlanner.Empty, result.Reason);
        Assert.Empty(f.Packets);
        session.Interact(null);
        Assert.NotEqual(first, session.Rows[0].Output.Id);
        Assert.True(session.Rows[0].Evaluation.CanCraft);
    }

    [Fact]
    public void HoverFreezesAutomaticAlternativeUntilPointerLeavesAndPinsSurviveRefresh()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        second.Output = first.Output;
        f.Register(first, second);
        f.Snapshot(a);
        var session = Session(f, first, second);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        session.Interact(row.Output.Id);
        f.Snapshot(b);
        session.Refresh();
        Assert.Same(first, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.False(Assert.Single(session.Rows).Evaluation.CanCraft);
        session.Interact(null);
        Assert.Same(second, Assert.Single(session.Rows).Recipe.Recipe);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        session.Refresh();
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(first, Assert.Single(session.Rows).Recipe.Recipe);
        session.Auto();
        Assert.False(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(second, Assert.Single(session.Rows).Recipe.Recipe);
    }

    [Fact]
    public void UnsupportedAlternativesRemainInspectableAndCannotDispatchEitherMode()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        recipe.CopyAttributesFrom = "I";
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        Assert.Null(row.Evaluation.Relevance);
        Assert.Empty(row.Evaluation.Missing);
        Assert.Equal(CraftingDispatchStatus.Rejected, session.Activate(row.Output.Id, false).Status);
        Assert.Equal(CraftingDispatchStatus.Rejected, session.Activate(row.Output.Id, true).Status);
        string text = CraftingMenuTooltip.Describe(row, key => key, stack => stack.Collectible.Code.ToString());
        Assert.Contains("craftingmenu-unknown", text);
        Assert.Contains(row.Evaluation.UnsupportedReason!, text);
        Assert.DoesNotContain("craftingmenu-missing", text);
    }

    [Fact]
    public void TooltipListsAllOneExecutionDeficitsAndAllowedChoicesWithoutMaximumBatchDeficits()
    {
        var f = new AllocationFixture();
        var wildcard = f.Wildcard("board-*", 4);
        wildcard.AllowedVariants = new[] { "oak", "pine" };
        wildcard.SkipVariants = new[] { "rotten" };
        var stick = f.Stack(2, "stick", 1);
        var recipe = f.Recipe(wildcard, f.Ingredient(stick, 2));
        f.Register(recipe);
        f.Snapshot(f.Stack(1, "board-oak", 3), stick);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        string text = CraftingMenuTooltip.Describe(row, key => key, stack => stack.Collectible.Code.ToString());
        Assert.Equal(2, row.Evaluation.Missing.Length);
        Assert.Equal(2, text.Split("craftingmenu-missing").Length - 1);
        Assert.Contains("oak, pine", text);
        Assert.Contains("rotten", text);
        Assert.Contains("craftingmenu-tool", CraftingMenuTooltip.Describe(
            WithTool(f), key => key, stack => stack.Collectible.Code.ToString()));
    }

    [Fact]
    public void SearchRetainsCatalogueIncludingZeroStockAndReportsEmptyResults()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.Equal(0, row.Evaluation.Allocated);
        session.Search("  " + row.Recipe.DisplayName.ToUpperInvariant() + " ");
        Assert.Single(session.Rows);
        session.Search("nothing-matches-this");
        Assert.Empty(session.Rows);
        session.Search(" ");
        Assert.Single(session.Rows);
    }

    [Fact]
    public void NativeTileControlRoutesKeyboardAndMouseShiftWithoutTakingOutput()
    {
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var first = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        var second = f.Recipe(f.Ingredient(f.Stack(2, "b", 1)));
        f.Register(first, second);
        var session = Session(f, first, second);
        session.Refresh();
        var api = new Mock<ICoreClientAPI>();
        var input = new Mock<IInputAPI>();
        var keys = new bool[256];
        input.SetupGet(value => value.KeyboardKeyStateRaw).Returns(keys);
        api.SetupGet(value => value.Input).Returns(input.Object);
        var actions = new List<(int Output, bool Shift)>();
        CraftingRecipeSelection? inspected = null;
        var bounds = ElementBounds.Fixed(0, 0, 96, 96).WithEmptyParent();
        bounds.CalcWorldBounds();
        using var icons = new CraftingIconCache(api.Object);
        using var tiles = new GuiElementCraftingTiles(api.Object, ElementBounds.Fixed(0, 0, 96, 96).WithParent(bounds),
            bounds, session,
            row => inspected = row, (id, shift) => actions.Add((id, shift)), icons);
        PrepareTiles(tiles);
        tiles.OnFocusGained();
        Assert.NotNull(inspected);
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter, ShiftPressed = true };
        tiles.OnKeyDown(api.Object, enter);
        Assert.True(enter.Handled);
        Assert.True(Assert.Single(actions).Shift);
        tiles.OnMouseMove(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        var move = new KeyEvent { KeyCode = (int)GlKeys.Right };
        tiles.OnKeyDown(api.Object, move);
        Assert.True(move.Handled);
        Assert.Equal(session.Rows[1].Output.Id, inspected!.Output.Id);
        Assert.Equal(session.Rows[1].Output.Id, inspected!.Output.Id);
        tiles.OnMouseMove(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.Equal(session.Rows[0].Output.Id, inspected!.Output.Id);
        tiles.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Space });
        Assert.False(actions[1].Shift);
        Assert.Equal(session.Rows[1].Output.Id, actions[1].Output);
        Assert.Equal(session.Rows[1].Output.Id, inspected!.Output.Id);
        keys[(int)GlKeys.ShiftLeft] = true;
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.True(actions[2].Shift);
        Assert.Equal(session.Rows[0].Output.Id, actions[2].Output);
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        tiles.OnMouseUp(api.Object, new MouseEvent(1000, 1000, EnumMouseButton.Left, 0));
        Assert.Equal(3, actions.Count);
        tiles.OnFocusLost();
        Assert.Null(inspected);
        Assert.True(f.Grid[9].Empty);
    }

    [Fact]
    public void NativeScrollbarAndKeyboardKeepFocusedTilesVisibleWithoutUndoingRefreshDeferral()
    {
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 12).Select(id =>
            f.Recipe(f.Ingredient(f.Stack(id, "ingredient-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = Session(f, recipes);
        session.Refresh();
        var api = new Mock<ICoreClientAPI>();
        var input = new Mock<IInputAPI>();
        api.SetupGet(value => value.Input).Returns(input.Object);
        input.SetupGet(value => value.MouseX).Returns(10);
        input.SetupGet(value => value.MouseY).Returns(10);
        var bounds = ElementBounds.Fixed(0, 0, 96, 96).WithEmptyParent();
        bounds.CalcWorldBounds();
        CraftingRecipeSelection? inspected = null;
        using var icons = new CraftingIconCache(api.Object);
        using var tiles = new GuiElementCraftingTiles(api.Object, ElementBounds.Fixed(0, 0, 96, 96).WithParent(bounds),
            bounds, session,
            row => inspected = row, (_, _) => { }, icons);
        PrepareTiles(tiles);
        var scrollbarBounds = ElementBounds.Fixed(100, 0, 12, 96).WithEmptyParent();
        scrollbarBounds.CalcWorldBounds();
        using var scrollbar = new HeadlessScrollbar(api.Object, tiles.Scroll, scrollbarBounds);
        tiles.Bind(scrollbar);
        tiles.OnFocusGained();
        for (int i = 0; i < 4; i++)
            tiles.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Down });
        Assert.True(scrollbar.CurrentYPosition > 0);
        float position = scrollbar.CurrentYPosition;
        int focused = inspected!.Output.Id;
        session.Refresh();
        Assert.Equal(position, scrollbar.CurrentYPosition);
        Assert.Equal(focused, inspected!.Output.Id);
        var wheel = new MouseWheelEventArgs { deltaPrecise = -1 };
        tiles.OnMouseWheel(api.Object, wheel);
        Assert.True(wheel.IsHandled);
        Assert.True(scrollbar.CurrentYPosition >= position);
        session.Search("no-matches");
        Assert.Empty(session.Rows);
        Assert.Equal(0, scrollbar.CurrentYPosition);
    }

    [Theory]
    [InlineData(1280, 720, 1)]
    [InlineData(1280, 720, 2)]
    [InlineData(800, 600, 1)]
    [InlineData(800, 600, 2)]
    [InlineData(1920, 1080, 1.5)]
    public void LayoutStaysInsideViewportAndOffGridAtRepresentativeScales(int width, int height, double scale)
    {
        using var guiScale = new GuiScaleScope((float)scale);
        var window = Calculated(ElementBounds.Fixed(0, 0, width / scale, height / scale).WithEmptyParent());
        var parent = Calculated(ElementBounds.Fixed(width * .2 / scale, height * .1 / scale,
            width * .6 / scale, height * .8 / scale).WithParent(window));
        var grid = Calculated(ElementBounds.Fixed(width * .4 / scale, height * .1 / scale,
            width * .18 / scale, height * .5 / scale).WithParent(parent));
        var rect = CraftingMenuLayout.Place(parent, grid, grid, window, 200, 164);
        Assert.NotNull(rect);
        rect.CalcWorldBounds();
        Assert.Same(parent, rect.ParentBounds);
        Assert.InRange(rect.absX, 0, width - rect.OuterWidth);
        Assert.InRange(rect.absY, 0, height - rect.OuterHeight);
        Assert.True(rect.absX + rect.OuterWidth <= grid.absX || rect.absX >= grid.absX + grid.OuterWidth
            || rect.absY + rect.OuterHeight <= grid.absY || rect.absY >= grid.absY + grid.OuterHeight);
        Assert.True(rect.OuterWidth >= 200 * scale);
        Assert.True(rect.OuterHeight >= 164 * scale);
    }

    [Fact]
    public void LayoutChangesSidesAndReportsImpossibleViewportRatherThanCoveringGrid()
    {
        using var scale = new GuiScaleScope();
        var window = Calculated(ElementBounds.Fixed(0, 0, 1200, 700).WithEmptyParent());
        var parent = Calculated(ElementBounds.Fixed(200, 30, 500, 500).WithFixedPadding(4).WithParent(window));
        var grid = Calculated(ElementBounds.Fixed(300, 70, 150, 200).WithParent(parent));
        var right = CraftingMenuLayout.Place(parent, grid, grid, window, 200, 164)!;
        right.CalcWorldBounds();
        Assert.True(right.absX >= parent.absX + parent.OuterWidth);
        double before = right.absX;
        parent.fixedX += 20;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        Assert.Equal(before + 20, right.absX);
        parent.fixedX = 650;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        var left = CraftingMenuLayout.Place(parent, grid, grid, window, 200, 164)!;
        left.CalcWorldBounds();
        Assert.True(left.absX + left.OuterWidth <= parent.absX);
        var tiny = Calculated(ElementBounds.Fixed(0, 0, 100, 100).WithEmptyParent());
        Assert.Null(CraftingMenuLayout.Place(tiny, tiny, tiny, tiny, 200, 164));
    }

    [Fact]
    public void NativeContainerRecyclesBoundedButtonsAndTabsAcrossVirtualRowsInBothDirections()
    {
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 256).Select(id =>
            f.Recipe(f.Ingredient(f.Stack(id, "ingredient-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = Session(f, recipes);
        session.Refresh();
        var api = new Mock<ICoreClientAPI>();
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 96, 72).WithEmptyParent());
        CraftingRecipeSelection? inspected = null;
        using var icons = new CraftingIconCache(api.Object);
        using var tiles = new GuiElementCraftingTiles(api.Object,
            ElementBounds.Fixed(0, 0, 96, 72).WithParent(viewport), viewport, session,
            row => inspected = row, (_, _) => { }, icons);
        PrepareTiles(tiles);
        var pool = tiles.Elements.ToArray();
        Assert.Equal(6, pool.Length);
        Assert.All(pool, tile =>
        {
            Assert.IsAssignableFrom<GuiElementTextButton>(tile);
            Assert.Same(viewport, tile.InsideClipBounds);
        });
        tiles.OnFocusGained();
        for (int i = 1; i <= 30; i++)
        {
            var tab = new KeyEvent { KeyCode = (int)GlKeys.Tab };
            tiles.OnKeyDown(api.Object, tab);
            Assert.True(tab.Handled);
            Assert.Equal(session.Rows[i].Output.Id, inspected!.Output.Id);
            var focused = tiles.CurrentTabIndexElement;
            Assert.InRange(focused.Bounds.absY, viewport.absY, viewport.absY + viewport.InnerHeight - 46);
        }
        for (int i = 29; i >= 0; i--)
        {
            var tab = new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = true };
            tiles.OnKeyDown(api.Object, tab);
            Assert.True(tab.Handled);
            Assert.Equal(session.Rows[i].Output.Id, inspected!.Output.Id);
        }
        var leave = new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = true };
        tiles.OnKeyDown(api.Object, leave);
        Assert.False(leave.Handled);
        tiles.OnFocusLost();
        tiles.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = true });
        tiles.OnFocusGained();
        Assert.Equal(session.Rows[^1].Output.Id, inspected!.Output.Id);
        var leaveAtEnd = new KeyEvent { KeyCode = (int)GlKeys.Tab };
        tiles.OnKeyDown(api.Object, leaveAtEnd);
        Assert.False(leaveAtEnd.Handled);
        tiles.OnFocusLost();
        tiles.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Tab });
        tiles.OnFocusGained();
        Assert.Equal(session.Rows[0].Output.Id, inspected!.Output.Id);
        Assert.Equal(pool, tiles.Elements);
        session.Search("no-matches");
        Assert.False(tiles.Focusable);
        Assert.False(tiles.HasFocus);
        Assert.Null(tiles.CurrentTabIndexElement);
        tiles.OnFocusGained();
        Assert.False(tiles.HasFocus);
    }

    [Fact]
    public void NativeClippingRejectsOverscanClicksAndPressedButtonCannotBeReboundByScrolling()
    {
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 12).Select(id =>
            f.Recipe(f.Ingredient(f.Stack(id, "ingredient-" + id, 1)))).ToArray();
        f.Register(recipes);
        var session = Session(f, recipes);
        session.Refresh();
        var api = new Mock<ICoreClientAPI>();
        var input = new Mock<IInputAPI>();
        api.SetupGet(value => value.Input).Returns(input.Object);
        input.SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 96, 72).WithEmptyParent());
        var actions = new List<int>();
        using var icons = new CraftingIconCache(api.Object);
        using var tiles = new GuiElementCraftingTiles(api.Object,
            ElementBounds.Fixed(0, 0, 96, 72).WithParent(viewport), viewport, session,
            _ => { }, (id, _) => actions.Add(id), icons);
        PrepareTiles(tiles);
        var outside = new MouseEvent(10, 100, EnumMouseButton.Left, 0);
        tiles.OnMouseDown(api.Object, outside);
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 100, EnumMouseButton.Left, 0));
        Assert.False(outside.Handled);
        Assert.Empty(actions);
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Right, 0));
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Right, 0));
        Assert.Empty(actions);
        int pressed = session.Rows[0].Output.Id;
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        tiles.OnFocusGained();
        tiles.Scroll(96);
        tiles.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Down });
        session.Refresh();
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.Equal(pressed, Assert.Single(actions));
        tiles.Scroll(96);
        Assert.Equal(session.Rows[4], ((GuiElementCraftingTile)tiles.Elements[0]).Selection);
        tiles.Scroll(40);
        int partial = session.Rows[4].Output.Id;
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 60, EnumMouseButton.Left, 0));
        Assert.Equal(-40, tiles.Bounds.absY);
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 60, EnumMouseButton.Left, 0));
        Assert.Equal(partial, actions[1]);
    }

    [Fact]
    public void ProductionPolicyDoesNotInspectOrRejectCustomEventSubscriptions()
    {
        var f = new AllocationFixture();
        var loader = new Mock<IModLoader>();
        loader.Setup(mods => mods.GetModSystem<CharacterSystem>(true)).Returns(new CharacterSystem());
        f.Api.Setup(api => api.ModLoader).Returns(loader.Object);
        var events = (ClientEventAPI)RuntimeHelpers.GetUninitializedObject(typeof(ClientEventAPI));
        events.MatchesRecipe += (_, _, _) => true;
        events.MatchesGridRecipe += (_, _, _, _) => false;
        f.Api.Setup(api => api.Event).Returns(events);
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        f.Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe> { recipe });
        var policy = new CraftingClientPolicy(f.Api.Object, f.Player);
        Assert.True(policy.CanAccess(recipe));
        f.Api.VerifyGet(api => api.Event, Times.Never);
    }

    [Fact]
    public void PolicyUsesNativeTraitsForRecipeAccess()
    {
        var f = new AllocationFixture();
        var characters = new CharacterSystem();
        var loader = new Mock<IModLoader>();
        loader.Setup(mods => mods.GetModSystem<CharacterSystem>(true)).Returns(characters);
        f.Api.Setup(api => api.ModLoader).Returns(loader.Object);
        var first = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        var policy = new CraftingClientPolicy(f.Api.Object, f.Player);
        Assert.Null(first.RequiresTrait);
        Assert.True(policy.CanAccess(first));
        first.RequiresTrait = "unavailable";
        // Native CharacterSystem intentionally permits an absent character class.
        Assert.True(policy.CanAccess(first));
        f.Player.Entity.WatchedAttributes.SetString("characterClass", "unknown");
        Assert.False(policy.CanAccess(first));
    }

    [Theory]
    [InlineData("same-code", false, false)]
    [InlineData("same-code", false, true)]
    [InlineData("same-code", true, false)]
    [InlineData("same-code", true, true)]
    [InlineData("wildcard", false, false)]
    [InlineData("wildcard", false, true)]
    [InlineData("wildcard", true, false)]
    [InlineData("wildcard", true, true)]
    [InlineData("custom", false, false)]
    [InlineData("custom", false, true)]
    [InlineData("custom", true, false)]
    [InlineData("custom", true, true)]
    public void ProductionCatalogueAllowsNativePlacementDespitePotentialCompetition(
        string competition, bool partial, bool shift)
    {
        using var f = new TransferFixture(2, partial ? 1 : 64);
        var selected = f.Entry.Recipe;
        var other = f.F.Recipe(competition == "wildcard" ? f.F.Wildcard("*") : f.F.Ingredient(f.Ingredient(1)));
        other.Shapeless = competition == "wildcard";
        if (competition == "custom")
            other = new CustomGridRecipe {
                Name = other.Name, Width = 1, Height = 1, Output = other.Output,
                ResolvedIngredients = new[] { f.F.Ingredient(f.F.Stack(999, "unrelated", 1)) }
            };
        foreach (var recipe in new[] { selected, other })
        {
            var output = recipe.Output!.ResolvedItemStack!;
            recipe.Output.ResolvedItemStack = new ItemStack(new NamedOutputItem(output.Id) {
                Code = output.Collectible.Code
            });
        }
        f.F.Register(selected, other);

        var api = new Mock<ICoreClientAPI>();
        var world = new Mock<IClientWorldAccessor>();
        var loader = new Mock<IModLoader>();
        loader.Setup(mods => mods.GetModSystem<CharacterSystem>(true)).Returns(new CharacterSystem());
        api.SetupGet(value => value.World).Returns(world.Object);
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        world.SetupGet(value => value.Player).Returns(f.F.Player);
        world.SetupGet(value => value.GridRecipes).Returns(new List<GridRecipe> { selected, other });
        world.SetupGet(value => value.FastSearchRecipesByIngredient).Returns(f.F.Catalogue.Index);
        world.SetupGet(value => value.Calendar).Returns(Mock.Of<IClientGameCalendar>());
        var (catalogue, allocator) = CraftingMenuSystem.BuildCatalogue(api.Object);
        using var coordinator = new CraftingTransferCoordinator(f.F.Player, world.Object, allocator,
            f.Packets.Add, f.Notifications.Add, f.RefreshQueue.Enqueue, Environment.CurrentManagedThreadId, true);
        var session = new CraftingMenuSession(catalogue, allocator, f.Snapshot, coordinator.Execute);
        session.Refresh();
        var row = Assert.Single(session.Rows.Where(row => ReferenceEquals(row.Recipe.Recipe, selected)));
        Assert.True(row.Evaluation.IsSupported);
        Assert.Equal(!partial, row.Evaluation.CanCraft);
        Assert.True(row.CanPlace);
        Assert.Equal(partial ? 1 : 0, row.Evaluation.MissingUnits);
        Assert.Equal(CraftingDispatchStatus.Dispatched, session.Activate(row.Output.Id, shift).Status);
        Assert.Equal(partial ? 1 : shift ? 64 : 2, f.F.Grid[0].StackSize);
        Assert.True(f.F.Grid[9].Empty);
        Assert.NotEmpty(f.Packets);
        Assert.Empty(f.Notifications);
    }

    private sealed class CustomGridRecipe : GridRecipe { }

    private sealed class NamedOutputItem(int id) : MockItem(id)
    {
        public override string GetHeldItemName(ItemStack stack) => Code.ToString();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TagChoicesDescribeNativeInnerAndOuterJunctionsAndForbiddenTags(bool disjunctive)
    {
        var f = new AllocationFixture();
        var first = Tag(1, 2);
        var second = Tag(3);
        var forbidden = Tag(4);
        var ingredient = f.Wildcard("*", 2);
        ingredient.Tags = new ComplexTagCondition<TagSet>
        {
            isDisjunctive = disjunctive,
            conditions = new[]
            {
                new ComplexTagCondition<TagSet>.Condition { RequiredTags = first, ForbiddenTags = forbidden },
                new ComplexTagCondition<TagSet>.Condition { RequiredTags = second }
            }
        };
        Assert.True(ingredient.Tags.Matches(disjunctive ? Tag(1, 2) : Tag(1, 3)));
        Assert.False(ingredient.Tags.Matches(disjunctive ? Tag(1) : Tag(1, 2)));
        Assert.False(ingredient.Tags.Matches(disjunctive ? Tag(1, 2, 4) : Tag(1, 3, 4)));
        var recipe = f.Recipe(ingredient);
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        string text = CraftingMenuTooltip.Describe(row, key => key, stack => stack.Collectible.Code.ToString(),
            tags => tags.Equals(first) ? new[] { "game:wood", "game:board" }
                : tags.Equals(second) ? new[] { "game:metal" } : new[] { "game:rotten" });
        Assert.Contains(disjunctive ? "craftingmenu-tags-all" : "craftingmenu-tags-any", text);
        Assert.Contains(disjunctive ? "craftingmenu-or" : "craftingmenu-and", text);
        Assert.Contains("craftingmenu-tags-none game:rotten", text);
        Assert.Contains("game:board, game:wood", text);
        Assert.Contains("game:metal", text);
        Assert.Equal(2, text.Split("craftingmenu-tags-none").Length - 1);
        Assert.Equal(2, Assert.Single(row.Evaluation.Missing).Quantity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyRequiredTagChoiceExplainsNativeMeaningInsteadOfInventingAcceptedTags(bool disjunctive)
    {
        var f = new AllocationFixture();
        var ingredient = f.Wildcard("*");
        ingredient.Tags = new ComplexTagCondition<TagSet>
        {
            isDisjunctive = disjunctive,
            conditions = new[] { new ComplexTagCondition<TagSet>.Condition { ForbiddenTags = Tag(1) } }
        };
        var recipe = f.Recipe(ingredient);
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        string text = CraftingMenuTooltip.Describe(Assert.Single(session.Rows), key => key,
            stack => stack.Collectible.Code.ToString(), _ => new[] { "game:rotten" });
        Assert.Contains(disjunctive ? "craftingmenu-tags-unrestricted" : "craftingmenu-tags-impossible", text);
        Assert.Contains("craftingmenu-tags-none game:rotten", text);
    }

    private static TagSet Tag(params ushort[] handles)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write((ushort)handles.Length);
        foreach (ushort handle in handles) writer.Write(handle);
        writer.Flush();
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);
        return TagSet.FromBytes(reader);
    }

    private static CraftingMenuSession Session(AllocationFixture f, params GridRecipe[] recipes) =>
        new(f.Catalogue.Build(recipes), f.Allocator,
            () => CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            (_, _) => throw new InvalidOperationException("This test must not dispatch."));

    private static CraftingRecipeSelection WithTool(AllocationFixture f)
    {
        var ingredient = f.Ingredient(f.Stack(3, "tool", 1));
        ingredient.Consume = false;
        ingredient.ToolDurabilityCost = 0;
        var recipe = f.Recipe(ingredient);
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        return Assert.Single(session.Rows);
    }

    private sealed class GuiScaleScope : IDisposable
    {
        private readonly float previous = RuntimeEnv.GUIScale;
        internal GuiScaleScope(float scale = 1) => RuntimeEnv.GUIScale = scale;
        public void Dispose() => RuntimeEnv.GUIScale = previous;
    }

    private static ElementBounds Calculated(ElementBounds bounds)
    {
        bounds.CalcWorldBounds();
        return bounds;
    }

    private static void PrepareTiles(GuiElementCraftingTiles tiles)
    {
        foreach (GuiElementCraftingTile tile in tiles.Elements) tile.PlaySound = false;
        tiles.BeforeCalcBounds();
        tiles.Bounds.CalcWorldBounds();
    }

    private sealed class HeadlessScrollbar(ICoreClientAPI api, Action<float> changed, ElementBounds bounds)
        : GuiElementScrollbar(api, changed, bounds)
    {
        public override void RecomposeHandle() { }
    }
}

[CollectionDefinition("Crafting native GUI", DisableParallelization = true)]
public sealed class CraftingGuiCollection;
