using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookTilesInteractionTests
{
    [Theory]
    [InlineData("output", false)]
    [InlineData("alternative", false)]
    [InlineData("reintroduced", false)]
    [InlineData("output", true)]
    [InlineData("alternative", true)]
    [InlineData("reintroduced", true)]
    public void HeldReleaseCannotActivateReboundTileAndRequiresNewPress(string change, bool craftable)
    {
        // Arrange a visible tile and press it before the backing selection changes.
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        if (change == "alternative") second.Output = first.Output;
        f.Register(first, second);
        f.Snapshot(a);
        using var session = Session(f, first, second);
        using var filter = craftable ? null
            : session.Filters.Register("test:inventory", new PredicateFilter(row => row.Evaluation.CanCraft));
        session.CraftableOnly.SetEnabled(craftable);
        session.Refresh();
        var api = new Mock<ICoreClientAPI>();
        var input = new Mock<IInputAPI>();
        input.SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        api.SetupGet(value => value.Input).Returns(input.Object);
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 107, 72).WithEmptyParent());
        var actions = new List<int>();
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object,
            ElementBounds.Fixed(0, 0, 107, 72).WithParent(viewport), viewport, session,
            _ => { }, (id, _) => actions.Add(id), icons);
        PrepareTiles(tiles);
        var tile = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        var original = tile.Selection!;
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.True(tile.Pressed);

        // Act: change inventory or filter membership, forcing the held press to be reconciled.
        if (change == "reintroduced")
        {
            if (craftable) f.Snapshot();
            else filter!.Replace(new PredicateFilter(_ => false));
            session.Refresh();
            Assert.Empty(session.Rows);
            Assert.False(tile.Pressed);
            if (craftable) f.Snapshot(a);
            else filter!.Replace(new PredicateFilter(row => row.Evaluation.CanCraft));
        }
        else f.Snapshot(b);
        session.Refresh();

        // Assert refresh canceled the old press before the pointer's held release arrives.
        Assert.False(tile.Pressed);
        Assert.NotNull(tile.Selection);
        if (change == "output") Assert.NotEqual(original.Output.Id, tile.Selection.Output.Id);
        if (change == "alternative")
        {
            Assert.Equal(original.Output.Id, tile.Selection.Output.Id);
            Assert.NotEqual(original.Recipe.Id, tile.Selection.Recipe.Id);
        }
        if (change == "reintroduced") Assert.Equal(original.Recipe.Id, tile.Selection.Recipe.Id);

        // Act: release the old gesture, then make a fresh press against the rebound tile.
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.Empty(actions);
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));

        // Assert only the new gesture dispatches the current selection.
        Assert.Equal(tile.Selection.Output.Id, Assert.Single(actions));
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.Single(actions);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void LiveMembershipRebindsHoverAndFocusAndRecoversFromEmpty(bool focus, bool inventory, bool craftable)
    {
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        f.Register(first, second);
        f.Snapshot(a);
        using var session = Session(f, first, second);
        using var filter = craftable ? null : session.Filters.Register("test:membership", new PredicateFilter(row => inventory
            ? row.Evaluation.CanCraft : row.Recipe.Recipe == first));
        session.CraftableOnly.SetEnabled(craftable);
        session.Refresh();
        var api = new Mock<ICoreClientAPI>();
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 107, 72).WithEmptyParent());
        CraftingRecipeSelection? inspected = null;
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object,
            ElementBounds.Fixed(0, 0, 107, 72).WithParent(viewport), viewport, session,
            row => inspected = row, (_, _) => { }, icons);
        PrepareTiles(tiles);
        if (focus) tiles.OnFocusGained();
        else tiles.UpdateHover(10, 10);
        Assert.Same(first, inspected!.Recipe.Recipe);
        if (inventory) f.Snapshot(b);
        else filter!.Replace(new PredicateFilter(row => row.Recipe.Recipe == second));
        session.Refresh();
        Assert.Same(second, inspected!.Recipe.Recipe);
        if (focus)
        {
            Assert.True(tiles.HasFocus);
            Assert.Equal(inspected, Assert.IsType<GuiElementRecipeBookTile>(tiles.CurrentTabIndexElement).Selection);
        }
        if (craftable) f.Snapshot();
        else filter!.Replace(new PredicateFilter(_ => false));
        session.Refresh();
        Assert.Null(inspected);
        Assert.False(tiles.HasFocus);
        Assert.Null(tiles.CurrentTabIndexElement);
        Assert.All(tiles.Elements, tile => Assert.False(tile.Focusable));
        if (craftable) f.Snapshot(b);
        else filter!.Replace(new PredicateFilter(row => row.Recipe.Recipe == second));
        session.Refresh();
        if (focus) tiles.OnFocusGained();
        Assert.Same(second, inspected!.Recipe.Recipe);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShrinkingScrolledContentClampsBothOffsetsEvenDuringPress(bool retainPressed)
    {
        using var scale = new GuiScaleScope();
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 12).Select(id =>
            f.Recipe(f.Ingredient(f.Stack(id, "ingredient-" + id, 1)))).ToArray();
        f.Register(recipes);
        using var session = Session(f, recipes);
        session.Refresh();
        using var filter = session.Filters.Register("test:membership", new PredicateFilter(_ => true));
        var api = new Mock<ICoreClientAPI>();
        var input = new Mock<IInputAPI>();
        input.SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        api.SetupGet(value => value.Input).Returns(input.Object);
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 107, 72).WithEmptyParent());
        var actions = new List<int>();
        CraftingRecipeSelection? inspected = null;
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object,
            ElementBounds.Fixed(0, 0, 107, 72).WithParent(viewport), viewport, session,
            row => inspected = row, (id, _) => actions.Add(id), icons);
        PrepareTiles(tiles);
        using var scrollbar = new HeadlessScrollbar(api.Object, tiles.Scroll,
            Calculated(ElementBounds.Fixed(115, 0, 12, 72).WithEmptyParent()));
        tiles.Bind(scrollbar);
        tiles.Scroll(150);
        Assert.Equal(150, scrollbar.CurrentYPosition, 3);
        int pressed = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]).Selection!.Output.Id;
        int retained = retainPressed ? pressed : session.Rows[0].Output.Id;
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        tiles.OnFocusGained();
        Assert.True(((GuiElementRecipeBookTile)tiles.Elements[0]).Pressed);
        filter.Replace(new PredicateFilter(row => row.Output.Id == retained));
        session.Refresh();
        Assert.Equal(0, scrollbar.CurrentYPosition);
        Assert.Equal(0, tiles.Bounds.absY);
        Assert.Equal(retained, Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]).Selection!.Output.Id);
        Assert.Equal(retained, inspected!.Output.Id);
        Assert.Equal(retainPressed, ((GuiElementRecipeBookTile)tiles.Elements[0]).Pressed);
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        if (retainPressed) Assert.Equal(retained, Assert.Single(actions));
        else Assert.Empty(actions);
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
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 107, 72).WithEmptyParent());
        CraftingRecipeSelection? inspected = null;
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object,
            ElementBounds.Fixed(0, 0, 107, 72).WithParent(viewport), viewport, session,
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
            Assert.InRange(focused.Bounds.absY, viewport.absY, viewport.absY + viewport.InnerHeight - 45);
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
        var viewport = Calculated(ElementBounds.Fixed(0, 0, 107, 72).WithEmptyParent());
        var actions = new List<int>();
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object,
            ElementBounds.Fixed(0, 0, 107, 72).WithParent(viewport), viewport, session,
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
        tiles.Scroll(100);
        tiles.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Down });
        session.Refresh();
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        Assert.Equal(pressed, Assert.Single(actions));
        tiles.Scroll(100);
        Assert.Equal(session.Rows[4], ((GuiElementRecipeBookTile)tiles.Elements[0]).Selection);
        tiles.Scroll(60);
        int partial = session.Rows[4].Output.Id;
        tiles.OnMouseDown(api.Object, new MouseEvent(10, 60, EnumMouseButton.Left, 0));
        Assert.Equal(0, tiles.Bounds.absY);
        tiles.OnMouseUp(api.Object, new MouseEvent(10, 60, EnumMouseButton.Left, 0));
        Assert.Equal(partial, actions[1]);
    }
}
