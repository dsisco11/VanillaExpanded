using Cairo;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Integration.Gui;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Trait(TestCapabilities.TraitName, TestCapabilities.Cairo)]
[Collection("Recipe book native GUI")]
public sealed class GuiControlContextStateIntegrationTests
{
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
}
