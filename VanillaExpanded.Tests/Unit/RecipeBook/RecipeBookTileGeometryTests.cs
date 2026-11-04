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
    [InlineData(57)]
    [InlineData(100)]
    [InlineData(106.999)]
    [InlineData(107)]
    [InlineData(156.999)]
    [InlineData(157)]
    [InlineData(300)]
    [InlineData(450)]
    public void MaximumFittingColumnsRemainContainedAndExtendContent(double width)
    {
        var geometry = new RecipeBookTileGeometry(width);
        double usableWidth = width - 2 * RecipeBookTileGeometry.HorizontalPadding;
        double occupiedWidth = geometry.Columns * geometry.Size + (geometry.Columns - 1) * RecipeBookTileGeometry.Gap;
        double oneMoreColumnWidth = (geometry.Columns + 1) * geometry.Size + geometry.Columns * RecipeBookTileGeometry.Gap;
        Assert.True(occupiedWidth <= usableWidth);
        Assert.True(oneMoreColumnWidth > usableWidth);

        double previousRight = 0;
        for (int column = 0; column < geometry.Columns; column++)
        {
            var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
            geometry.Place(bounds, column, 0, 0, 1);
            Assert.True(bounds.fixedX >= 0);
            Assert.True(bounds.fixedX + bounds.fixedWidth <= width);
            if (column > 0) Assert.True(bounds.fixedX >= previousRight);
            previousRight = bounds.fixedX + bounds.fixedWidth;
        }

        Assert.Equal(0, geometry.ContentHeight(0));
        Assert.True(geometry.ContentHeight(geometry.Columns) > 0);
        Assert.True(geometry.ContentHeight(geometry.Columns + 1) > geometry.ContentHeight(geometry.Columns));
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
    public void ConfiguredTileSizeFitsTheViewportAndExtendsContent(int tileSize)
    {
        // Arrange
        var geometry = new RecipeBookTileGeometry(tileSize + 12, tileSize);
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();

        // Act
        geometry.Place(bounds, 0, 1, 0, 1);

        // Assert
        Assert.Equal(tileSize, geometry.Size);
        Assert.Equal(1, geometry.Columns);
        Assert.Equal(0, geometry.ContentHeight(0));
        Assert.True(geometry.ContentHeight(1) >= tileSize);
        Assert.True(geometry.ContentHeight(2) > geometry.ContentHeight(1));
        Assert.True(bounds.fixedX >= 0);
        Assert.True(bounds.fixedY >= 0);
        Assert.True(bounds.fixedX + bounds.fixedWidth <= tileSize + 12);
        Assert.True(bounds.fixedY + bounds.fixedHeight <= geometry.ContentHeight(2));
        Assert.Equal(tileSize, bounds.fixedWidth, 6);
        Assert.Equal(tileSize, bounds.fixedHeight, 6);
    }

    [Theory]
    [InlineData(29)]
    [InlineData(91)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TileSizeOutsideTheSupportedRangeFailsExplicitly(double tileSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeBookTileGeometry(200, tileSize));
}
