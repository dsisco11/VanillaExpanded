using System;
using System.Linq;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookTileGeometryTests
{
    [Theory]
    [InlineData(42, 1, 6)]
    [InlineData(60, 1, 15)]
    [InlineData(76.999, 1, 23.4995)]
    [InlineData(77, 2, 6)]
    [InlineData(111.999, 2, 23.4995)]
    [InlineData(112, 3, 6)]
    [InlineData(300, 8, 12.5)]
    [InlineData(450, 12, 17.5)]
    public void MaximumFittingColumnsAreCenteredAndEdgesAreSnappedWithoutAccumulatedDrift(
        double width, int columns, double leading)
    {
        var geometry = new RecipeBookTileGeometry(width);
        Assert.Equal(columns, geometry.Columns);
        Assert.Equal(leading, geometry.Leading, 6);
        Assert.True(columns * 30 + (columns - 1) * 5 + 12 <= width);
        Assert.True((columns + 1) * 30 + columns * 5 + 12 > width);
        foreach (double scale in new[] { 1, 1.25, 1.5, 1.75, 2 })
        {
            double previousRight = 0;
            for (int column = 0; column < columns; column++)
            {
                var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
                geometry.Place(bounds, column, 17, 7.3, scale);
                Assert.Equal(Math.Round((leading + column * 35) * scale, MidpointRounding.AwayFromZero),
                    bounds.fixedX * scale, 8);
                Assert.Equal(Math.Round((17 * 35 - 7.3) * scale, MidpointRounding.AwayFromZero),
                    bounds.fixedY * scale, 8);
                Assert.Equal(Math.Round((leading + column * 35 + 30) * scale, MidpointRounding.AwayFromZero),
                    (bounds.fixedX + bounds.fixedWidth) * scale, 8);
                Assert.Equal(Math.Round((17 * 35 - 7.3 + 30) * scale, MidpointRounding.AwayFromZero),
                    (bounds.fixedY + bounds.fixedHeight) * scale, 8);
                if (column > 0) Assert.InRange(bounds.fixedX * scale - previousRight, Math.Floor(5 * scale) - .000001, Math.Ceiling(5 * scale) + .000001);
                previousRight = (bounds.fixedX + bounds.fixedWidth) * scale;
            }
            Assert.InRange(Math.Abs(Math.Round(leading * scale, MidpointRounding.AwayFromZero)
                - (width * scale - previousRight)), 0, 1.001);
        }
        Assert.Equal(0, geometry.ContentHeight(0));
        Assert.Equal(30, geometry.ContentHeight(columns));
        Assert.Equal(65, geometry.ContentHeight(columns + 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41.999)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AViewportThatCannotFitOnePaddedTileFailsExplicitly(double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeBookTileGeometry(width));

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
        using var original = Create(112);
        original.Scroll(112 * scale);
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
        using var wider = Create(182);
        wider.RestoreView(view);
        Assert.Equal(42, wider.ScrollPosition, 4);
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
        using var narrower = Create(77);
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
        Assert.Equal((45 * 35 - 5) - 65, narrower.ScrollPosition, 3);
        session.Search("no matches");
        Assert.Equal(0, narrower.ScrollPosition);
        Assert.Null(inspected);
        Assert.All(narrower.Elements.Cast<GuiElementRecipeBookTile>(), tile => Assert.False(tile.Visible));

        GuiElementRecipeBookTiles Create(double width)
        {
            var viewport = ElementBounds.Fixed(0, 0, width, 65).WithEmptyParent();
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
        var viewport = ElementBounds.Fixed(3.37, 5.81, 112, 65).WithEmptyParent();
        viewport.CalcWorldBounds();
        using var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
            ElementBounds.Fixed(0, 0, 112, 100).WithParent(viewport), viewport, session,
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
