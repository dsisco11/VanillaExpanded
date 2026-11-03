using System;
using System.Linq;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
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
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(90)]
    public void ConfiguredTileSizeDrivesLayoutAndPreviewSizing(int tileSize)
    {
        // Arrange
        var geometry = new RecipeBookTileGeometry(tileSize + 12, tileSize);
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();

        // Act
        geometry.Place(bounds, 0, 1, 0, 1);

        // Assert
        Assert.Equal(45, new RecipeBookTileGeometry(57).Size);
        Assert.Equal(tileSize, geometry.Size);
        Assert.Equal(tileSize + 5, geometry.RowPitch);
        Assert.Equal(1, geometry.Columns);
        Assert.Equal(6, geometry.Leading, 6);
        Assert.Equal(0, geometry.ContentHeight(0));
        Assert.Equal(tileSize + 2, geometry.ContentHeight(1), 6);
        Assert.Equal(2 * tileSize + 7, geometry.ContentHeight(2), 6);
        Assert.Equal(6, bounds.fixedX, 6);
        Assert.Equal(tileSize + 7, bounds.fixedY, 6);
        Assert.Equal(tileSize, bounds.fixedWidth, 6);
        Assert.Equal(tileSize, bounds.fixedHeight, 6);
        Assert.Equal(Math.Max(2, (int)Math.Ceiling((tileSize - 2) / 2d) * 2),
            RecipeBookTileGeometry.PreviewSize(1, tileSize));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(91)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TileSizeOutsideTheSupportedRangeFailsExplicitly(double tileSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeBookTileGeometry(200, tileSize));
}
