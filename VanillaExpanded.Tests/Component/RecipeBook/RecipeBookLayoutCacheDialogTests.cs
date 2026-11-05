using System.Reflection;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Trait(TestCapabilities.TraitName, TestCapabilities.Cairo)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookLayoutCacheDialogTests
{
    [Fact]
    public void DialogCacheMethodsMeasureValidatedSettingsAndAcceptComposedBounds()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1.25f);
        var settings = new Dictionary<string, int>
        {
            [RecipeBookSystem.TileSizeSetting] = (int)RecipeBookVisualMetrics.TileSize
        };
        using var fixture = new MenuFixture(scale: 1.25f, savedIntSettings: settings);
        fixture.Parent.TryOpen();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);
        var composer = book.SingleComposer;

        // Act: opening has already measured once; exercise the cache boundary without repeating work.
        Assert.False(book.EnsureLayoutMeasurements());
        double initialMinimumHeight = ReadDouble(book, "minimumHeight");
        Assert.False(book.EnsureLayoutMeasurements());

        var inventoryComposer = fixture.Parent.Composers["maininventory"];
        var grid = inventoryComposer.GetElement("craftinggrid").Bounds;
        var output = inventoryComposer.GetElement("outputslot").Bounds;
        var placement = book.EnsureLayoutPlacement(false, composer.Bounds, grid, output);
        Assert.NotNull(placement);
        book.AcceptComposedLayout();
        var accepted = book.EnsureLayoutPlacement(false, composer.Bounds, grid, output);

        settings[RecipeBookSystem.TileSizeSetting] = RecipeBookVisualMetrics.MaximumTileSize;
        Assert.True(book.EnsureLayoutMeasurements());
        double expandedMinimumHeight = ReadDouble(book, "minimumHeight");
        Assert.Equal(RecipeBookVisualMetrics.MaximumTileSize, ReadInt(book, "tileSize"));
        Assert.True(expandedMinimumHeight > initialMinimumHeight);
        Assert.False(book.EnsureLayoutMeasurements());

        // Invalid persisted values are normalized before they enter the measurement key.
        settings[RecipeBookSystem.TileSizeSetting] = RecipeBookVisualMetrics.MaximumTileSize + 1;
        bool invalidSettingMeasured = book.EnsureLayoutMeasurements();

        // Assert
        Assert.NotNull(accepted);
        Assert.Equal(composer.Bounds.fixedX, accepted!.Value.X);
        Assert.Equal(composer.Bounds.fixedY, accepted.Value.Y);
        Assert.Equal(composer.Bounds.fixedWidth, accepted.Value.Width);
        Assert.Equal(composer.Bounds.fixedHeight, accepted.Value.Height);
        Assert.True(invalidSettingMeasured);
        Assert.Equal((int)RecipeBookVisualMetrics.TileSize, ReadInt(book, "tileSize"));
        Assert.Equal((int)RecipeBookVisualMetrics.TileSize, settings[RecipeBookSystem.TileSizeSetting]);
        Assert.False(book.EnsureLayoutMeasurements());
    }

    [Fact]
    public void DialogMeasurementSnapshotUsesTheLiveGuiScale()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1.25f);
        using var fixture = new MenuFixture(scale: 1.25f);
        fixture.Parent.TryOpen();
        fixture.Toggle();
        var book = Assert.Single(fixture.Menus);

        // Act
        Assert.False(book.EnsureLayoutMeasurements());
        Assert.False(book.EnsureLayoutMeasurements());
        RuntimeEnv.GUIScale = 1.75f;
        bool changedScaleMeasured = book.EnsureLayoutMeasurements();

        // Assert
        Assert.True(changedScaleMeasured);
        Assert.False(book.EnsureLayoutMeasurements());
    }

    private static double ReadDouble(GuiDialogRecipeBook book, string name) =>
        (double)typeof(GuiDialogRecipeBook).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;

    private static int ReadInt(GuiDialogRecipeBook book, string name) =>
        (int)typeof(GuiDialogRecipeBook).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(book)!;
}
