using Cairo;
using VanillaExpanded.Gui;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Unit.RecipeBook;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.Gui;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class GuiControlVisualsTests
{
    public static IEnumerable<object[]> Palette => new object[][]
    {
        [GuiControlPalette.DialogTop, 0x4b4030], [GuiControlPalette.DialogBottom, 0x3b3226],
        [GuiControlPalette.DialogOutline, 0x817159], [GuiControlPalette.TitleTop, 0x65553e],
        [GuiControlPalette.TitleBottom, 0x4a3c2c], [GuiControlPalette.TitleSeparator, 0x2a221a],
        [GuiControlPalette.Text, 0xe8ddc5], [GuiControlPalette.PanelFill, 0x51432f],
        [GuiControlPalette.PanelOutline, 0x887453], [GuiControlPalette.TabFill, 0x382f23],
        [GuiControlPalette.TabOutline, 0x766349], [GuiControlPalette.HoverFill, 0x51422e],
        [GuiControlPalette.Icon, 0xb7a98c], [GuiControlPalette.HoverIcon, 0xfff0cc],
        [GuiControlPalette.SelectedIcon, 0xf5d58d], [GuiControlPalette.SelectedOutline, 0xaa8c54],
        [GuiControlPalette.SelectedAccent, 0xd3b46f], [GuiControlPalette.Heading, 0xc8b897],
        [GuiControlPalette.CheckboxFill, 0x30291e], [GuiControlPalette.CheckboxOutline, 0x948366],
        [GuiControlPalette.DropdownFill, 0x382f23], [GuiControlPalette.DropdownOutline, 0x74634a],
        [GuiControlPalette.DropdownText, 0xd7c8aa], [GuiControlPalette.SearchFill, 0x30281e],
        [GuiControlPalette.SearchOutline, 0x8c795b], [GuiControlPalette.Placeholder, 0xa99b80],
        [GuiControlPalette.ListFill, 0x352d22], [GuiControlPalette.ListOutline, 0x2c251c],
        [GuiControlPalette.ButtonText, 0xc9bba0], [GuiControlPalette.ButtonFill, 0x51412d],
        [GuiControlPalette.ButtonOutline, 0x756247]
    }.Select((entry, index) => new object[] { index, entry[0], entry[1] });

    [Theory]
    [MemberData(nameof(Palette))]
    public void PaletteMatchesApprovedReferenceAndReturnsIndependentColors(int role, int actual, int expected)
    {
        Assert.InRange(role, 0, 30);
        Assert.Equal(expected, actual);
        var color = GuiControlPalette.Color(actual);
        Assert.Equal(((expected >> 16) & 255) / 255d, color[0]);
        Assert.Equal(((expected >> 8) & 255) / 255d, color[1]);
        Assert.Equal((expected & 255) / 255d, color[2]);
        color[0] = 0;
        Assert.Equal(((expected >> 16) & 255) / 255d, GuiControlPalette.Color(actual)[0]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1.25, 1)]
    [InlineData(1.5, 2)]
    [InlineData(1.75, 2)]
    [InlineData(2, 2)]
    public void FrameHasApprovedFillAndGuiScaledWholePixelOutline(double scale, int thickness)
    {
        using var surface = new ImageSurface(Format.Argb32, 90, 90);
        using var ctx = new Context(surface);
        ctx.Antialias = Antialias.None;
        ctx.LineWidth = 3;
        GuiControlDrawing.Frame(ctx, 10.2, 10.2, 60, 60,
            GuiControlPalette.SearchFill, GuiControlPalette.SearchOutline, scale);
        Assert.Equal(0x30281e, Pixel(surface, 40, 40));
        for (int i = 0; i < thickness; i++) Assert.Equal(0x8c795b, Pixel(surface, 10 + i, 40));
        Assert.Equal(0x30281e, Pixel(surface, 10 + thickness, 40));
        Assert.Equal(3d, ctx.LineWidth);
    }

    [Fact]
    public void MetricsSeparateGenericControlsFromRecipeBookComposition()
    {
        using var graphics = new RecipeBookRenderFixture();
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
        Assert.Equal(30d, RecipeBookVisualMetrics.TileSize);
        Assert.Equal(5d, RecipeBookVisualMetrics.TileGap);
        Assert.Equal(6d, RecipeBookVisualMetrics.ListHorizontalPadding);
        Assert.Equal(3d, RecipeBookVisualMetrics.SelectionInset);
        Assert.Equal(8d, RecipeBookVisualMetrics.SelectionLength);
        var font = CairoFont.WhiteSmallText().WithFontSize(12);
        double expected = Math.Ceiling(font.GetTextExtents("A translated button label").Width + 16);
        Assert.Equal(expected, GuiControlMetrics.MeasureTextWidth(font, "A translated button label"));
    }

    [Fact]
    public void GradientsAreLocalAndInvalidPixelGeometryFailsExplicitly()
    {
        using var surface = new ImageSurface(Format.Argb32, 30, 300);
        using var ctx = new Context(surface);
        GuiControlDrawing.Gradient(ctx, 0, 0, 30, 300, GuiControlPalette.TitleTop, GuiControlPalette.TitleBottom);
        Assert.Equal(0x65553e, Pixel(surface, 10, 0));
        Assert.Equal(0x4a3c2c, Pixel(surface, 10, 299));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Pixels(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Pixels(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Pixels(1, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlPalette.Color(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlPalette.Color(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiControlDrawing.Frame(ctx, 0, 0, 0, 1, 0, 0, 1));
        using var graphics = new RecipeBookRenderFixture();
        Assert.Throws<ArgumentException>(() => GuiControlMetrics.InputBounds(ElementBounds.Fixed(0, 0, 20, 28)));
        Assert.Throws<ArgumentException>(() => GuiControlMetrics.InputBounds(ElementBounds.Fixed(0, 0, 100, 0)));
    }

    internal static int Pixel(ImageSurface surface, int x, int y)
    {
        surface.Flush();
        int offset = y * surface.Stride + x * 4;
        var pixels = surface.Data;
        return pixels[offset + 2] << 16 | pixels[offset + 1] << 8 | pixels[offset];
    }
}
