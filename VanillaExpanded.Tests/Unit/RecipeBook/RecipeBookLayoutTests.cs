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

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookLayoutTests
{
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
        var rect = RecipeBookLayout.Place(parent, grid, grid, window, 200, 164);
        Assert.NotNull(rect);
        rect.CalcWorldBounds();
        Assert.Same(window, rect.ParentBounds);
        Assert.InRange(rect.absX, 0, width - rect.OuterWidth);
        Assert.InRange(rect.absY, 0, height - rect.OuterHeight);
        Assert.True(rect.absX + rect.OuterWidth <= grid.absX || rect.absX >= grid.absX + grid.OuterWidth
            || rect.absY + rect.OuterHeight <= grid.absY || rect.absY >= grid.absY + grid.OuterHeight);
        Assert.True(rect.OuterWidth >= 200 * scale);
        Assert.True(rect.OuterHeight >= 164 * scale);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void LayoutChangesSidesAndReportsImpossibleViewportRatherThanCoveringGrid(float uiScale)
    {
        using var scale = new GuiScaleScope(uiScale);
        var window = Calculated(ElementBounds.Fixed(0, 0, 1200, 700).WithEmptyParent());
        var parent = Calculated(ElementBounds.Fixed(200, 30, 500, 500).WithFixedPadding(4).WithParent(window));
        var grid = Calculated(ElementBounds.Fixed(300, 70, 150, 200).WithParent(parent));
        var right = RecipeBookLayout.Place(parent, grid, grid, window, 200, 164)!;
        right.CalcWorldBounds();
        Assert.Equal(2, right.absX - parent.absX - parent.OuterWidth, 4);
        double before = right.absX;
        parent.fixedX += 20;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        Assert.Equal(before, right.absX);
        var moved = RecipeBookLayout.Place(parent, grid, grid, window, 200, 164)!;
        moved.CalcWorldBounds();
        Assert.Equal(before + 20 * uiScale, moved.absX, 4);
        Assert.Equal(2, moved.absX - parent.absX - parent.OuterWidth, 4);
        parent.fixedX = 650;
        parent.MarkDirtyRecursive();
        parent.CalcWorldBounds();
        var left = RecipeBookLayout.Place(parent, grid, grid, window, 200, 164)!;
        left.CalcWorldBounds();
        Assert.Equal(2, parent.absX - left.absX - left.OuterWidth, 4);
        var tiny = Calculated(ElementBounds.Fixed(0, 0, 100, 100).WithEmptyParent());
        Assert.Null(RecipeBookLayout.Place(tiny, tiny, tiny, tiny, 200, 164));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MovableLayoutClampsPositionAndSizeToWindowAndRejectsInsufficientSpace(float scale)
    {
        using var guiScale = new GuiScaleScope(scale);
        var window = Calculated(ElementBounds.Fixed(0, 0, 800, 600).WithEmptyParent());
        var bounds = RecipeBookLayout.PlaceMovable(ElementBounds.Fixed(-20, 550, 320, 420),
            window, 300, 250)!;
        bounds.CalcWorldBounds();
        Assert.Same(window, bounds.ParentBounds);
        Assert.Equal(0, bounds.absX, 4);
        Assert.Equal(180 * scale, bounds.absY, 4);
        Assert.Equal(320 * scale, bounds.OuterWidth, 4);
        Assert.Equal(420 * scale, bounds.OuterHeight, 4);
        bounds = RecipeBookLayout.PlaceMovable(ElementBounds.Fixed(900, -10, 1000, 700),
            window, 300, 250)!;
        bounds.CalcWorldBounds();
        Assert.Equal(0, bounds.absX, 4);
        Assert.Equal(0, bounds.absY, 4);
        Assert.Equal(window.InnerWidth, bounds.OuterWidth, 4);
        Assert.Equal(window.InnerHeight, bounds.OuterHeight, 4);
        Assert.Null(RecipeBookLayout.PlaceMovable(bounds, window, 801, 250));
        Assert.Null(RecipeBookLayout.PlaceMovable(bounds, window, 300, 601));
    }
}
