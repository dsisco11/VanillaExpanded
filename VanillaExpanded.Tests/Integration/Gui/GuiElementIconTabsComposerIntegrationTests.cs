using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.Gui.GuiElementIconTabsTestsTestSupport;

namespace VanillaExpanded.Tests.Integration.Gui;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class GuiElementIconTabsComposerIntegrationTests
{
    [Theory]
    [InlineData(.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void NativeComposerOwnsIconOnlyButtonsAndBoundsAndRecompositionDisposesTextures(float scale)
    {
        using var graphics = new GuiRenderFixture(scale);
        Setup(graphics);
        using var tabs = Create(graphics, ["First", "A much longer localized name", "Third"], 500, _ => { });
        var composer = Assert.IsType<GuiComposer>(tabs.Composer);
        Assert.False(composer.Tabbable);
        Assert.Same(tabs.Bounds, composer.Bounds.ParentBounds);
        Assert.Same(composer.Bounds, tabs.Viewport!.ParentBounds);
        var first = composer.GetToggleButton("tab-0");
        var second = composer.GetToggleButton("tab-1");
        Assert.Equal("", first.GetText());
        Assert.Equal("", second.GetText());
        Assert.Equal(first.Bounds.OuterWidth, second.Bounds.OuterWidth);
        Assert.Equal(GuiElementIconTabs.TabSize * scale, first.Bounds.OuterWidth, 4);
        Assert.Equal(tabs.Bounds.absX, first.Bounds.absX, 4);
        Assert.Equal(first.Bounds.absX + first.Bounds.OuterWidth + 4 * scale, second.Bounds.absX, 4);
        Assert.Equal(tabs.Bounds.absY + tabs.Bounds.InnerHeight,
            first.Bounds.absY + first.Bounds.OuterHeight, 4);
        Assert.Equal(GuiElementIconTabs.TabHeight * scale, first.Bounds.OuterHeight, 4);
        Assert.Equal("A much longer localized name", tabs.Hints[1].GetText());
        int textureCount = graphics.Textures.Count;
        Assert.True(textureCount > 0);
        tabs.SetValue(2, false);
        Compose(tabs);
        Assert.Equal(textureCount, graphics.Textures.Count);
        Assert.Equal(2, tabs.activeElement);
        Assert.True(tabs.Composer!.GetToggleButton("tab-2").On);
        Assert.Single(tabs.Bounds.ChildBounds);
        tabs.Dispose();
        tabs.Dispose();
        Assert.Empty(tabs.Bounds.ChildBounds);
        Assert.Empty(graphics.Textures);
        graphics.Render.Verify(value => value.RenderItemStackToAtlas(It.IsAny<ItemStack>(),
            It.IsAny<ITextureAtlasAPI>(), It.IsAny<int>(), It.IsAny<Action<int>>(),
            It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()), Times.Never);
    }
}
