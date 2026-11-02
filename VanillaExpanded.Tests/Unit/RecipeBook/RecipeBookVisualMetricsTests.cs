using Cairo;
using VanillaExpanded.Gui;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookVisualMetricsTests
{
    [Fact]
    public void MetricsSeparateGenericControlsFromRecipeBookComposition()
    {
        using var graphics = new GuiRenderFixture();
        Assert.Equal(17d, GuiControlMetrics.CheckboxSize);
        Assert.Equal(28d, GuiControlMetrics.SearchHeight);
        Assert.Equal(14f, GuiControlMetrics.TextFontSize);
        Assert.Equal(12f, GuiControlMetrics.CompactFontSize);
        Assert.Equal(30d, GuiControlMetrics.TabWidth);
        Assert.Equal(36d, GuiControlMetrics.TabHeight);
        Assert.Equal(32d, GuiControlMetrics.InactiveTabHeight);
        Assert.Equal(4d, GuiControlMetrics.TabGap);
        Assert.Equal(20d, GuiControlMetrics.CategoryIconSize);
        Assert.Equal(2d, GuiControlMetrics.TabAccent);
        Assert.Equal(5d, GuiControlMetrics.TabCornerRadius);
        Assert.Equal(7d, GuiControlMetrics.CategoryIconBottomInset);
        Assert.Equal(30d, GuiControlMetrics.NativeDropdownHeight);
        Assert.Equal(30d, GuiControlMetrics.NativePopupRowHeight);
        Assert.Equal(.35, GuiControlPalette.DisabledOpacity);
        Assert.Equal(340d, RecipeBookVisualMetrics.PreferredWidth);
        Assert.Equal(420d, RecipeBookVisualMetrics.PreferredHeight);
        Assert.Equal(36d, RecipeBookVisualMetrics.TitleHeight);
        Assert.Equal(12d, RecipeBookVisualMetrics.TitleSpacing);
        Assert.Equal(8d, RecipeBookVisualMetrics.OuterPadding);
        Assert.Equal(8d, RecipeBookVisualMetrics.PanelPadding);
        Assert.Equal(12d, RecipeBookVisualMetrics.PanelTopPadding);
        Assert.Equal(11f, RecipeBookVisualMetrics.HeadingFontSize);
        Assert.Equal(17d, RecipeBookVisualMetrics.HeadingHeight);
        Assert.Equal(8d, RecipeBookVisualMetrics.HeadingSpacing);
        Assert.Equal(10d, RecipeBookVisualMetrics.SearchSpacing);
        Assert.Equal(12d, RecipeBookVisualMetrics.FooterSpacing);
        Assert.Equal(45d, RecipeBookVisualMetrics.TileSize);
        Assert.Equal(5d, RecipeBookVisualMetrics.TileGap);
        Assert.Equal(6d, RecipeBookVisualMetrics.ListHorizontalPadding);
        Assert.Equal(3d, RecipeBookVisualMetrics.SelectionInset);
        Assert.Equal(8d, RecipeBookVisualMetrics.SelectionLength);
        var font = CairoFont.WhiteSmallText().WithFontSize(12);
        double expected = Math.Ceiling(font.GetTextExtents("A translated button label").Width + 16);
        Assert.Equal(expected, GuiControlMetrics.MeasureTextWidth(font, "A translated button label"));
    }
}
