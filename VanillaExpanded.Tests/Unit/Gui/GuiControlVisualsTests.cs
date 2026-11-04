using Cairo;
using VanillaExpanded.Gui;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.Unit.Gui;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class GuiControlVisualsTests
{
    [Theory]
    [InlineData(0x123456, 0.25, 18 / 255d, 52 / 255d, 86 / 255d)]
    [InlineData(0xabcdef, 1, 171 / 255d, 205 / 255d, 239 / 255d)]
    public void ColorConversionUsesCallerValueAndReturnsIndependentChannels(
        int rgb, double opacity, double red, double green, double blue)
    {
        var expected = new[] { red, green, blue, opacity };
        var first = GuiControlPalette.Color(rgb, opacity);
        Assert.Equal(expected, first);
        first[0] = 0;
        var second = GuiControlPalette.Color(rgb, opacity);
        Assert.NotSame(first, second);
        Assert.Equal(expected, second);
    }

    [Fact]
    public void FrameRestoresCallerContextState()
    {
        // Arrange
        using var surface = new ImageSurface(Format.Argb32, 8, 8);
        using var ctx = new Context(surface);
        ctx.LineWidth = 3;

        // Act
        GuiControlDrawing.Frame(ctx, 0, 0, 8, 8,
            GuiControlPalette.SearchFill, GuiControlPalette.SearchOutline, 1);

        // Assert
        Assert.Equal(3d, ctx.LineWidth);
    }

    [Fact]
    public void PixelConversionRejectsInvalidUnitsAndScale()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Pixels(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Pixels(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Pixels(1, double.NaN));
    }

    [Fact]
    public void ColorConversionRejectsInvalidOpacity()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlPalette.Color(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlPalette.Color(0, 2));
    }

    [Fact]
    public void FrameRejectsInvalidRectangleDimensions()
    {
        // Arrange
        // Frame validates dimensions before accessing the context, so Cairo setup is unnecessary.
        Context ctx = null!;

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Frame(ctx, 0, 0, 0, 1, 0, 0, 1));
    }

    [Fact]
    public void InputBoundsRejectInsufficientEditableOrClearAreas()
    {
        // Arrange
        using var graphics = new GuiRenderFixture();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => GuiControlMetrics.InputBounds(ElementBounds.Fixed(0, 0, 20, 28)));
        Assert.Throws<ArgumentException>(() => GuiControlMetrics.InputBounds(ElementBounds.Fixed(0, 0, 100, 0)));
    }
}
