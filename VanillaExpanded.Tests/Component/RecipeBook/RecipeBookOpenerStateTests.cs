using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;
using VanillaExpanded.Tests;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookOpenerStateTests
{
    [Fact]
    public void FailedMenuOpeningLeavesBookTabVisibleAndUsable()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.ResizePanelSpace(320, 120);
        f.Toggle();
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        Assert.True(f.ToggleButton.Focusable);
        Assert.False(f.ToggleButton.On);
        f.ResizePanelSpace(320, 420);
        f.Toggle();
        Assert.True(Assert.Single(f.Menus).IsOpened());
        f.AssertToggleOpen();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void OpeningReleaseDoesNotImmediatelyToggleTheRecipeBookClosed(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        f.ResizePanelSpace(150, 1080);
        int x = (int)(f.ToggleButton.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(f.ToggleButton.Bounds.absY + GuiElement.scaled(5));
        var down = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        f.Parent.OnMouseDown(down);
        Assert.True(down.Handled);
        Assert.Empty(f.Menus);
        var up = new MouseEvent(x, y, EnumMouseButton.Left, 0);
        f.Parent.OnMouseUp(up);
        var menu = Assert.Single(f.Menus);
        menu.OnMouseUp(up);
        Assert.Same(menu, Assert.Single(f.Menus));
        Assert.True(menu.IsOpened());
        f.AssertToggleOpen();
        f.Toggle();
        Assert.Empty(f.Menus);
        Assert.True(f.ToggleButton.Visible);
        Assert.False(f.ToggleButton.On);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CancelledBookButtonPressPreservesTheOpenState(bool initiallyOpen, bool loseFocus)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        if (initiallyOpen) f.Toggle();
        var button = f.ToggleButton;
        int x = (int)(button.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(button.Bounds.absY + GuiElement.scaled(5));
        f.Parent.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        if (loseFocus) button.OnFocusLost();
        else x = (int)(button.Bounds.absX - GuiElement.scaled(5));
        f.Parent.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));

        Assert.Equal(initiallyOpen, f.Menus.Any(menu => menu.IsOpened()));
        Assert.Equal(initiallyOpen, button.On);
        f.Toggle();
        Assert.Equal(!initiallyOpen, f.Menus.Any(menu => menu.IsOpened()));
        Assert.Equal(!initiallyOpen, button.On);
    }
}
