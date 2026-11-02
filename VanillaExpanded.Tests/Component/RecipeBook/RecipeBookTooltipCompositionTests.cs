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
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookTooltipCompositionTests
{
    [Theory]
    [InlineData(1, 300)]
    [InlineData(1, 204)]
    [InlineData(1.5, 300)]
    [InlineData(1.5, 204)]
    [InlineData(2, 300)]
    [InlineData(2, 204)]
    public void IngredientSummaryWrapsBelowNativeGridAndRefreshesOnlyWhenCoverageChanges(float scale, double width)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var board = f.Lifecycle.F.Stack(1, "board", 3);
        var stick = f.Lifecycle.F.Stack(2, "stick", 2);
        const string name = "Oak planks with a long localized name <font color=\"red\">literal</font>";
        var item = new Mock<MockItem>(board.Id, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        item.Object.Code = board.Item.Code;
        item.Setup(value => value.GetHeldItemName(It.IsAny<ItemStack>())).Returns(name);
        board = new ItemStack(item.Object, board.StackSize);
        var stickItem = new Mock<MockItem>(stick.Id, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        stickItem.Object.Code = stick.Item.Code;
        stickItem.Setup(value => value.GetHeldItemName(It.IsAny<ItemStack>())).Returns("Sticks");
        stick = new ItemStack(stickItem.Object, stick.StackSize);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        recipe.Width = 2;
        recipe.Height = 1;
        recipe.ResolvedIngredients = [f.Lifecycle.F.Ingredient(board, 4), f.Lifecycle.F.Ingredient(stick, 2)];
        var inventory = Assert.Single(((ClientPlayerInventoryManager)f.Lifecycle.F.Player.InventoryManager)
            .Inventories.Values.OfType<InventoryCraftingGrid>());
        inventory[0].Itemstack = board;
        inventory[1].Itemstack = stick;
        f.Lifecycle.F.Register(recipe);
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = width + 16;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        int baseline = f.TextureCount;
        var tiles = Assert.IsType<GuiElementRecipeBookTiles>(menu.SingleComposer.GetElement("tiles"));
        var tileBounds = tiles.Elements[0].Bounds;
        tiles.UpdateHover((int)(tileBounds.absX + 5), (int)(tileBounds.absY + 5));
        var popup = tooltip.Popup!;
        int shown = f.TextureCount;
        var content = popup.GetRichtext("recipe");
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        var text = content.Components.OfType<RichTextComponent>()
            .Where(component => component is not ClearFloatTextComponent).ToArray();
        Assert.Equal(new[] { "3/4", name, "2/2", stick.GetName() }, text.Select(component => component.DisplayText));
        Assert.Equal(GuiStyle.ErrorTextColor, text[0].Font.Color);
        Assert.Equal(CairoFont.WhiteSmallText().Color, text[2].Font.Color);
        Assert.All(text, component =>
        {
            Assert.Equal(CairoFont.WhiteDetailText().UnscaledFontsize, component.Font.UnscaledFontsize);
            Assert.True(component.Font.UnscaledFontsize < CairoFont.WhiteSmallText().UnscaledFontsize);
        });
        Assert.True(text[1].Lines.Length > 1);
        Assert.True(text[0].BoundsPerLine.Min(bounds => bounds.Y) >=
            grid.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height));
        Assert.True(text[2].BoundsPerLine.Min(bounds => bounds.Y) >=
            text.Take(2).SelectMany(component => component.BoundsPerLine).Max(bounds => bounds.Y + bounds.Height));
        Assert.All(text.SelectMany(component => component.BoundsPerLine), bounds =>
        {
            Assert.True(bounds.X >= 0);
            Assert.True(bounds.X + bounds.Width <= content.Bounds.InnerWidth + .001);
            Assert.True(content.Bounds.absY + bounds.Y + bounds.Height <=
                popup.Bounds.absY + popup.Bounds.InnerHeight + popup.Bounds.fixedPaddingY * scale + .001);
        });

        board.StackSize = 4;
        session.Refresh();
        Assert.NotSame(popup, tooltip.Popup);
        Assert.Equal(shown, f.TextureCount);
        popup = tooltip.Popup!;
        var fullCount = Assert.Single(popup.GetRichtext("recipe").Components.OfType<RichTextComponent>(),
            component => component.DisplayText == "4/4");
        Assert.Equal(CairoFont.WhiteSmallText().Color, fullCount.Font.Color);

        board.StackSize = 64;
        session.Refresh();
        Assert.Same(popup, tooltip.Popup);
        Assert.Equal(shown, f.TextureCount);

        inventory[0].Itemstack = null;
        session.Refresh();
        Assert.NotSame(popup, tooltip.Popup);
        var emptyCount = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components.OfType<RichTextComponent>(),
            component => component.DisplayText == "0/4");
        Assert.Equal(GuiStyle.ErrorTextColor, emptyCount.Font.Color);
        Assert.Equal(shown, f.TextureCount);
        tooltip.Dispose();
        Assert.Equal(baseline, f.TextureCount);
    }

    [Fact]
    public void UnresolvedRecipeTooltipShowsItsWarningWithoutConstructingAnInvalidNativeGrid()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var unresolved = row.Recipe.Recipe.Clone();
        unresolved.ResolvedIngredients = null;
        var entry = new CraftingRecipeEntry(1000, unresolved, row.Recipe.DisplayName);
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(row with
        {
            Recipe = entry,
            Evaluation = CraftingRecipeEvaluation.Unsupported(entry.UnresolvedReason!)
        });
        var components = tooltip.Popup!.GetRichtext("recipe").Components;
        Assert.Empty(components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.Contains(entry.UnresolvedReason!, string.Join("\n",
            components.OfType<RichTextComponent>().Select(component => component.DisplayText)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecipeTooltipOmitsEmptyDescriptionAndDividerWithoutLosingPreview(bool hasOutput)
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var recipe = row.Recipe.Recipe.Clone();
        var item = new Mock<MockItem>(555, (byte)0, f.Lifecycle.Api.Object) { CallBase = true };
        item.Object.Code = new AssetLocation("game:empty-description");
        item.Setup(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
                It.IsAny<IWorldAccessor>(), false))
            .Callback<ItemSlot, StringBuilder, IWorldAccessor, bool>((_, text, _, _) => text.Append(" \r\n"));
        recipe.Output!.ResolvedItemStack = hasOutput ? new ItemStack(item.Object) : null;
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(row with { Recipe = new CraftingRecipeEntry(1000, recipe, row.Recipe.DisplayName) });
        var popup = Assert.IsType<GuiComposer>(tooltip.Popup);
        var content = popup.GetRichtext("recipe");
        Assert.Single(popup.GetRichtext("title").Components);
        Assert.Null(popup.GetElement("description"));
        Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.Null(popup.GetElement("divider"));
        item.Verify(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
            It.IsAny<IWorldAccessor>(), false), hasOutput ? Times.Once() : Times.Never());
    }
}
