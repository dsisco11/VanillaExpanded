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

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookTileNativeInputTests
{
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
        var bounds = ElementBounds.Fixed(0, 0, 107, 96).WithEmptyParent();
        bounds.CalcWorldBounds();
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object, ElementBounds.Fixed(0, 0, 107, 96).WithParent(bounds),
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
        var bounds = ElementBounds.Fixed(0, 0, 107, 96).WithEmptyParent();
        bounds.CalcWorldBounds();
        CraftingRecipeSelection? inspected = null;
        using var icons = new RecipeBookIconCache(api.Object);
        using var tiles = new GuiElementRecipeBookTiles(api.Object, ElementBounds.Fixed(0, 0, 107, 96).WithParent(bounds),
            bounds, session,
            row => inspected = row, (_, _) => { }, icons);
        PrepareTiles(tiles);
        var scrollbarBounds = ElementBounds.Fixed(115, 0, 12, 96).WithEmptyParent();
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
}
