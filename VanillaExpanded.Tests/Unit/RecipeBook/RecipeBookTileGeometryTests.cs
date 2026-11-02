using System;
using System.Linq;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookTileGeometryTests
{
    [Theory]
    [InlineData(57, 1, 6)]
    [InlineData(100, 1, 27.5)]
    [InlineData(106.999, 1, 30.9995)]
    [InlineData(107, 2, 6)]
    [InlineData(156.999, 2, 30.9995)]
    [InlineData(157, 3, 6)]
    [InlineData(300, 5, 27.5)]
    [InlineData(450, 8, 27.5)]
    public void MaximumFittingColumnsAreCenteredAndEdgesAreSnappedWithoutAccumulatedDrift(
        double width, int columns, double leading)
    {
        var geometry = new RecipeBookTileGeometry(width);
        Assert.Equal(columns, geometry.Columns);
        Assert.Equal(leading, geometry.Leading, 6);
        Assert.True(columns * 45 + (columns - 1) * 5 + 12 <= width);
        Assert.True((columns + 1) * 45 + columns * 5 + 12 > width);
        foreach (double scale in new[] { 1, 1.25, 1.5, 1.75, 2 })
        {
            double previousRight = 0;
            for (int column = 0; column < columns; column++)
            {
                var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
                geometry.Place(bounds, column, 17, 7.3, scale);
                Assert.Equal(Math.Round((leading + column * 50) * scale, MidpointRounding.AwayFromZero),
                    bounds.fixedX * scale, 8);
                Assert.Equal(Math.Round((2 + 17 * 50 - 7.3) * scale, MidpointRounding.AwayFromZero),
                    bounds.fixedY * scale, 8);
                Assert.Equal(Math.Round((leading + column * 50 + 45) * scale, MidpointRounding.AwayFromZero),
                    (bounds.fixedX + bounds.fixedWidth) * scale, 8);
                Assert.Equal(Math.Round((2 + 17 * 50 - 7.3 + 45) * scale, MidpointRounding.AwayFromZero),
                    (bounds.fixedY + bounds.fixedHeight) * scale, 8);
                if (column > 0) Assert.InRange(bounds.fixedX * scale - previousRight, Math.Floor(5 * scale) - .000001, Math.Ceiling(5 * scale) + .000001);
                previousRight = (bounds.fixedX + bounds.fixedWidth) * scale;
            }
            Assert.InRange(Math.Abs(Math.Round(leading * scale, MidpointRounding.AwayFromZero)
                - (width * scale - previousRight)), 0, 1.001);
        }
        Assert.Equal(0, geometry.ContentHeight(0));
        Assert.Equal(47, geometry.ContentHeight(columns));
        Assert.Equal(97, geometry.ContentHeight(columns + 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(56.999)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AViewportThatCannotFitOnePaddedTileFailsExplicitly(double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeBookTileGeometry(width));

    [Theory]
    [InlineData(.75f, 47)]
    [InlineData(1f, 47)]
    [InlineData(1.25f, 47)]
    [InlineData(1.5f, 47)]
    [InlineData(2f, 47)]
    [InlineData(.75f, 95)]
    [InlineData(1f, 95)]
    [InlineData(1.25f, 95)]
    [InlineData(1.5f, 95)]
    [InlineData(2f, 95)]
    public void TopPaddingRejectsClicksAndNavigationAndScrollKeepFirstAndLastRecipesAccessible(float scale, double height)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Mock.Get(graphics.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 12)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        using var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        var actions = new List<int>();
        var viewport = ElementBounds.Fixed(0, 0, 137, height).WithEmptyParent();
        viewport.CalcWorldBounds();
        using var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
            ElementBounds.Fixed(0, 0, 137, height).WithParent(viewport), viewport, session,
            _ => { }, (id, _) => actions.Add(id), graphics.Cache, graphics.CreateShader);
        tiles.BeforeCalcBounds();
        tiles.Bounds.CalcWorldBounds();
        var first = Assert.IsType<GuiElementRecipeBookTile>(tiles.Elements[0]);
        Assert.Equal(Math.Round(2d * scale, MidpointRounding.AwayFromZero), first.PixelBounds.renderY);
        int x = (int)first.PixelBounds.renderX + 2;
        Click(x, 0);
        Assert.Empty(actions);
        Assert.False(first.Pressed);
        Click(x, (int)first.PixelBounds.renderY + 2);
        Assert.Equal(session.Rows[0].Output.Id, Assert.Single(actions));

        tiles.OnFocusGained();
        for (int row = 1; row < 6; row++) Navigate(GlKeys.Down, row * 2);
        for (int row = 4; row >= 0; row--) Navigate(GlKeys.Up, row * 2);
        Assert.Equal(0, tiles.ScrollPosition);
        Assert.Equal(Math.Round(2d * scale, MidpointRounding.AwayFromZero), first.PixelBounds.renderY);
        tiles.Scroll(float.MaxValue);
        Assert.Equal(297 - height, tiles.ScrollPosition, 3);
        var last = tiles.Elements.Cast<GuiElementRecipeBookTile>()
            .Single(tile => tile.Selection?.Output.Id == session.Rows[^1].Output.Id);
        Assert.True(last.IsInViewport);
        Click((int)(last.PixelBounds.renderX + last.PixelBounds.InnerWidth / 2),
            (int)(last.PixelBounds.renderY + last.PixelBounds.InnerHeight / 2));
        Assert.Equal(new[] { session.Rows[0].Output.Id, session.Rows[^1].Output.Id }, actions);

        void Click(int mouseX, int mouseY)
        {
            tiles.OnMouseDown(graphics.Api.Object, new MouseEvent(mouseX, mouseY, EnumMouseButton.Left, 0));
            tiles.OnMouseUp(graphics.Api.Object, new MouseEvent(mouseX, mouseY, EnumMouseButton.Left, 0));
        }

        void Navigate(GlKeys key, int index)
        {
            var args = new KeyEvent { KeyCode = (int)key };
            tiles.OnKeyDown(graphics.Api.Object, args);
            Assert.True(args.Handled);
            var focused = Assert.IsType<GuiElementRecipeBookTile>(tiles.CurrentTabIndexElement);
            Assert.Equal(session.Rows[index].Output.Id, focused.Selection!.Output.Id);
            Assert.True(focused.PixelBounds.renderY >= 0);
            Assert.True(focused.PixelBounds.renderY + focused.PixelBounds.InnerHeight <= Math.Ceiling(viewport.InnerHeight));
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1.25, false)]
    [InlineData(1.5, false)]
    [InlineData(1.75, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(1.25, true)]
    [InlineData(1.5, true)]
    [InlineData(1.75, true)]
    [InlineData(2, true)]
    public void ResizeRestoresRecipeRowAndInspectionAndDoesNotCarryAHeldPress(float scale, bool keyboard)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 90)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        using var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        session.Select(session.Rows[40].Output.Id);
        CraftingRecipeSelection? inspected = null;
        using var original = Create(157);
        original.Scroll(157 * scale);
        var anchor = Assert.IsType<GuiElementRecipeBookTile>(original.Elements[0]);
        int x = (int)anchor.PixelBounds.renderX + 5;
        int y = (int)(original.InsideClipBounds.renderY + 5 * scale);
        original.UpdateHover(x, y);
        var view = original.CaptureView();
        Assert.Equal(session.Rows[9].Output.Id, view.AnchorOutput);
        Assert.Equal(view.AnchorOutput, view.InspectedOutput);
        Assert.Equal(7, view.RowOffset, 4);
        anchor.OnMouseDownOnElement(graphics.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.True(anchor.Pressed);
        original.Dispose();
        using var wider = Create(257);
        wider.RestoreView(view);
        Assert.Equal(57, wider.ScrollPosition, 4);
        Assert.Equal(session.Rows[40].Output.Id, session.SelectedOutput);
        Assert.Equal(view.InspectedOutput, inspected!.Output.Id);
        Assert.All(wider.Elements.Cast<GuiElementRecipeBookTile>(), tile => Assert.False(tile.Pressed));
        var retained = wider.Elements.Cast<GuiElementRecipeBookTile>()
            .Single(tile => tile.Selection?.Output.Id == view.AnchorOutput);
        Assert.True(retained.IsInViewport);
        wider.OnFocusGained();
        retained.OnFocusGained();
        if (keyboard) wider.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Tab });
        var focusView = wider.CaptureView();
        Assert.Equal(keyboard, focusView.KeyboardFocus);
        wider.Dispose();
        using var narrower = Create(107);
        narrower.RestoreView(focusView);
        Assert.Equal(focusView.FocusedOutput,
            Assert.IsType<GuiElementRecipeBookTile>(narrower.CurrentTabIndexElement).Selection!.Output.Id);
        Assert.Equal(focusView.InspectedOutput, inspected!.Output.Id);
        graphics.Render.Invocations.Clear();
        narrower.RenderInteractiveElements(0);
        var outlines = graphics.Render.Invocations.Where(call => call.Method.Name == nameof(IRenderAPI.RenderRectangle)).ToArray();
        Assert.Equal(keyboard ? 1 : 0, outlines.Length);
        if (keyboard)
        {
            var bounds = narrower.CurrentTabIndexElement.Bounds;
            var outline = Assert.Single(outlines);
            Assert.Equal((int)bounds.renderX, Convert.ToInt32(outline.Arguments[0]));
            Assert.Equal((int)bounds.renderY, Convert.ToInt32(outline.Arguments[1]));
            Assert.Equal(800f, Convert.ToSingle(outline.Arguments[2]));
            Assert.Equal(bounds.OuterWidthInt, Convert.ToInt32(outline.Arguments[3]));
            Assert.Equal(bounds.OuterHeightInt, Convert.ToInt32(outline.Arguments[4]));
        }
        narrower.OnFocusLost();
        Assert.Null(inspected);
        narrower.Scroll(float.MaxValue);
        Assert.Equal((2 + 45 * 50 - 5) - 95, narrower.ScrollPosition, 3);
        session.Search("no matches");
        Assert.Equal(0, narrower.ScrollPosition);
        Assert.Null(inspected);
        Assert.All(narrower.Elements.Cast<GuiElementRecipeBookTile>(), tile => Assert.False(tile.Visible));

        GuiElementRecipeBookTiles Create(double width)
        {
            var viewport = ElementBounds.Fixed(0, 0, width, 95).WithEmptyParent();
            viewport.CalcWorldBounds();
            var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
                ElementBounds.Fixed(0, 0, width, 100).WithParent(viewport), viewport, session,
                row => inspected = row, (_, _) => { }, graphics.Cache, graphics.CreateShader);
            tiles.BeforeCalcBounds();
            tiles.Bounds.CalcWorldBounds();
            return tiles;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void FullHeightPartialRowsUseTheSameSnappedBoundsForIconsScissorsAndHitTesting(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(1, 30)
            .Select(id => f.Recipe(f.Ingredient(f.Stack(id, "input-" + id, 1)))).ToArray();
        f.Register(recipes);
        using var session = new RecipeBookSession(f.Catalogue.Build(recipes), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        var viewport = ElementBounds.Fixed(3.37, 5.81, 202, 95).WithEmptyParent();
        viewport.CalcWorldBounds();
        using var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
            ElementBounds.Fixed(0, 0, 202, 200).WithParent(viewport), viewport, session,
            _ => { }, (_, _) => { }, graphics.Cache, graphics.CreateShader);
        tiles.BeforeCalcBounds();
        tiles.Bounds.CalcWorldBounds();
        tiles.Scroll(12 * scale);
        var visible = tiles.Elements.Cast<GuiElementRecipeBookTile>().Where(tile => tile.IsInViewport).ToArray();
        Assert.Equal(9, visible.Length);
        Assert.True(visible[0].PixelBounds.renderY < viewport.renderY);
        Assert.True(visible[^1].PixelBounds.renderY + visible[^1].PixelBounds.InnerHeight > viewport.renderY + viewport.InnerHeight);
        foreach (var tile in visible)
        {
            Assert.Same(viewport, tile.InsideClipBounds);
            var pixels = tile.PixelBounds;
            int x = (int)pixels.renderX + 2;
            int y = (int)Math.Max(viewport.renderY + 2, pixels.renderY + 2);
            Assert.True(tile.IsPositionInside(x, y));
            Assert.False(tile.IsPositionInside(x, (int)viewport.renderY - 1));
            Assert.False(tile.IsPositionInside((int)(pixels.renderX + pixels.InnerWidth + 2), y));
        }
        tiles.RenderInteractiveElements(0);
        var program = Assert.Single(graphics.Programs);
        foreach (var tile in visible)
        {
            var pixels = tile.PixelBounds;
            program.VerifySet(value => value.TileBounds = new System.Numerics.Vector4((float)pixels.renderX,
                (float)pixels.renderY, (float)pixels.InnerWidth, (float)pixels.InnerHeight), Times.Once);
        }
        Assert.Empty(graphics.Clips);
    }
}
