using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.Gui.GuiStyledControlsTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.Gui;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class GuiStyledScrollbarValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ScrollbarRejectsNonPositiveOrNonFiniteCallerSteps(double step)
    {
        using var f = new ControlsFixture();
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GuiElementStyledScrollbar(f.Api, _ => { }, ElementBounds.Fixed(0, 0, 12, 180), step));
        Assert.Equal("arrowStep", error.ParamName);
        Assert.Empty(f.Graphics.Textures);
    }
}
