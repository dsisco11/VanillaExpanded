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

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookNativeTooltipIntegrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RecipeTooltipUsesNativeSelectedGridWithTitleAndReleasesReplacedPreviews(float scale)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var first = Assert.Single(f.Lifecycle.Recipes);
        first.Output!.Quantity = 4;
        var second = f.Lifecycle.F.Recipe(f.Lifecycle.F.Ingredient(f.Lifecycle.F.Stack(2, "stone", 1)));
        second.Output = first.Output.Clone();
        f.Lifecycle.Recipes.Add(second);
        f.Lifecycle.F.Register(first, second);
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        int texturesBefore = f.TextureCount;
        tooltip.Inspect(row);
        var popup = tooltip.Popup!;
        var content = popup.GetRichtext("recipe");
        var titleElement = popup.GetRichtext("title");
        var title = Assert.IsType<RichTextComponent>(Assert.Single(titleElement.Components));
        Assert.Equal(row.Recipe.DisplayName + " (x4)", title.DisplayText);
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        var preview = Assert.Single(grid.GridRecipesAndUnnamedIngredients).Recipe!;
        Assert.NotSame(row.Recipe.Recipe, preview);
        Assert.Equal(row.Recipe.Recipe.Name, preview.Name);
        Assert.NotSame(row.Recipe.Recipe.ResolvedIngredients![0], preview.ResolvedIngredients![0]);
        Assert.True(titleElement.Bounds.absY + title.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height) <=
            content.Bounds.absY + grid.BoundsPerLine[0].Y);
        int texturesShown = f.TextureCount;
        tooltip.Inspect(row);
        Assert.Same(popup, tooltip.Popup);
        Assert.Equal(texturesShown, f.TextureCount);
        tooltip.Inspect(row with { Evaluation = row.Evaluation with { }, Allocation = row.Allocation! with { } });
        Assert.Same(popup, tooltip.Popup);
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        f.SetMouse((int)(window.absX + window.InnerWidth - 2), (int)(window.absY + window.InnerHeight - 2));
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        render.Invocations.Clear();
        tooltip.RenderInteractiveElements(.1f);
        render.Verify(value => value.GlPushMatrix(), Times.Once);
        render.Verify(value => value.GlPopMatrix(), Times.Once);
        Assert.True(popup.Bounds.absX >= window.absX);
        Assert.True(popup.Bounds.absY >= window.absY);
        Assert.True(popup.Bounds.absX + popup.Bounds.OuterWidth <= window.absX + window.InnerWidth + .001);
        Assert.True(popup.Bounds.absY + popup.Bounds.OuterHeight <= window.absY + window.InnerHeight + .001);
        Assert.Empty(f.Lifecycle.Api.Object.Render.ScissorStack);
        session.Select(row.Output.Id);
        f.Click(menu.SingleComposer.GetElement("next"));
        var selected = Assert.Single(session.Rows);
        Assert.NotSame(row.Recipe, selected.Recipe);
        tooltip.Inspect(selected);
        Assert.NotSame(popup, tooltip.Popup);
        Assert.Equal(texturesShown, f.TextureCount);
        var selectedGrid = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components
            .OfType<SlideshowGridRecipeTextComponent>());
        Assert.Equal(selected.Recipe.Recipe.Name, Assert.Single(selectedGrid.GridRecipesAndUnnamedIngredients).Recipe!.Name);
        tooltip.Inspect(null);
        render.Invocations.Clear();
        tooltip.RenderInteractiveElements(.1f);
        Assert.Empty(render.Invocations);
        tooltip.Inspect(selected);
        render.Setup(value => value.GlTranslate(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Throws<InvalidOperationException>();
        Assert.Throws<InvalidOperationException>(() => tooltip.RenderInteractiveElements(.1f));
        render.Verify(value => value.GlPushMatrix(), Times.Once);
        render.Verify(value => value.GlPopMatrix(), Times.Once);
        tooltip.Dispose();
        Assert.Equal(texturesBefore, f.TextureCount);
    }

    [Theory]
    [InlineData(1, 220)]
    [InlineData(1.5, 330)]
    [InlineData(2, 440)]
    public void RecipeTooltipCentersWrappedTitlesAndVisibleGridCellsInNarrowWindows(float scale, double windowWidth)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var entry = new CraftingRecipeEntry(1000, row.Recipe.Recipe,
            "A long localized crafting output name that must wrap across several centered title lines");
        f.Lifecycle.Api.Object.Gui.WindowBounds.fixedWidth = windowWidth / scale;
        f.Lifecycle.Api.Object.Gui.WindowBounds.CalcWorldBounds();
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(row with { Recipe = entry });
        var popup = tooltip.Popup!;
        var content = popup.GetRichtext("recipe");
        var titleElement = popup.GetRichtext("title");
        var title = Assert.IsType<RichTextComponent>(Assert.Single(titleElement.Components));
        Assert.True(title.Lines.Length > 1);
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.All(title.BoundsPerLine, line =>
        {
            Assert.True(line.X >= 0);
            Assert.True(line.X + line.Width <= titleElement.Bounds.InnerWidth + .001);
        });
        Assert.True(grid.BoundsPerLine[0].X >= 0);
        Assert.True(grid.BoundsPerLine[0].X + grid.BoundsPerLine[0].Width <= content.Bounds.InnerWidth + .001);
        Assert.True(titleElement.Bounds.absY + title.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height) <=
            content.Bounds.absY + grid.BoundsPerLine[0].Y);
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(1.5, 300)]
    [InlineData(2, 300)]
    [InlineData(1, 204)]
    [InlineData(1.5, 204)]
    [InlineData(2, 204)]
    public void RecipeTooltipShowsNativeStatsAndFormattedBehaviorDetailsAboveDividerAndGrid(float scale, double width)
    {
        using var f = new MenuFixture(scale: scale, includeRecipes: true);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        var output = recipe.Output!.ResolvedItemStack!;
        output.Collectible.Durability = 50;
        output.Attributes.SetInt("durability", 35);
        ItemSlot? describedSlot = null;
        var behavior = new Mock<CollectibleBehavior>(output.Collectible);
        behavior.Setup(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
                f.Lifecycle.World.Object, false))
            .Callback<ItemSlot, StringBuilder, IWorldAccessor, bool>((slot, text, _, _) =>
            {
                describedSlot = slot;
                text.AppendLine("<font color=\"#ff0000\">Custom stat: 12</font>");
                text.AppendLine("These native item details wrap across multiple lines inside the padded description panel.");
                Assert.IsType<ItemStack>(slot.Itemstack).Attributes.SetBool("tooltip-mutated", true);
            });
        output.Collectible.CollectibleBehaviors = [behavior.Object];
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var row = Assert.Single(session.Rows);
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        int texturesBefore = f.TextureCount;
        var window = f.Lifecycle.Api.Object.Gui.WindowBounds;
        window.fixedWidth = width + 16;
        window.MarkDirtyRecursive();
        window.CalcWorldBounds();
        tooltip.Inspect(row);
        var popup = tooltip.Popup!;
        var content = popup.GetRichtext("recipe");
        var titleElement = popup.GetRichtext("title");
        var title = Assert.IsType<RichTextComponent>(Assert.Single(titleElement.Components));
        var description = popup.GetRichtext("description");
        var panel = description.Bounds.ParentBounds;
        AssertContained(panel, popup.Bounds);
        AssertContained(description.Bounds, panel);
        var text = description.Components.OfType<RichTextComponent>()
            .Where(component => component is not ClearFloatTextComponent).ToArray();
        Assert.Contains(text, component => component.Lines.Length > 1);
        Assert.Contains(text, component => component.DisplayText.Contains("Durability:"));
        var custom = Assert.Single(text, component => component.DisplayText.Contains("Custom stat: 12"));
        Assert.DoesNotContain(text, component => component.DisplayText.Contains("<font"));
        var grid = Assert.Single(content.Components.OfType<SlideshowGridRecipeTextComponent>());
        Assert.True(titleElement.Bounds.absY + title.BoundsPerLine.Max(bounds => bounds.Y + bounds.Height) <= panel.absY);
        Assert.True(description.Bounds.absY + text.SelectMany(component => component.BoundsPerLine)
            .Max(bounds => bounds.Y + bounds.Height) <= content.Bounds.absY + grid.BoundsPerLine[0].Y);
        Assert.All(text.SelectMany(component => component.BoundsPerLine), bounds =>
        {
            Assert.True(bounds.X >= 0);
            Assert.True(bounds.X + bounds.Width <= description.Bounds.InnerWidth + .001);
            Assert.True(bounds.Y >= 0);
            Assert.True(bounds.Y + bounds.Height <= description.Bounds.InnerHeight + .001);
        });
        Assert.NotNull(describedSlot);
        var describedStack = Assert.IsType<ItemStack>(describedSlot.Itemstack);
        Assert.NotSame(output, describedStack);
        Assert.IsType<DummyInventory>(describedSlot.Inventory);
        Assert.Equal(0, describedSlot.Inventory.GetTransitionSpeedMul(EnumTransitionType.Perish, describedStack));
        Assert.Equal(35, describedStack.Collectible.GetRemainingDurability(describedStack));
        Assert.False(output.Attributes.HasAttribute("tooltip-mutated"));
        Assert.False(row.Recipe.GetPreview()!.Attributes.HasAttribute("tooltip-mutated"));
        tooltip.Inspect(row);
        behavior.Verify(value => value.GetHeldItemInfo(It.IsAny<ItemSlot>(), It.IsAny<StringBuilder>(),
            f.Lifecycle.World.Object, false), Times.Once);
        tooltip.Dispose();
        Assert.Equal(texturesBefore, f.TextureCount);
    }

    [Fact]
    public void NativeTooltipGridResolvesAllowedWildcardIngredientChoices()
    {
        using var f = new MenuFixture(includeRecipes: true);
        var ingredient = f.Lifecycle.F.Wildcard("board-*", 2);
        ingredient.AllowedVariants = ["oak", "pine"];
        ingredient.SkipVariants = ["rotten"];
        var recipe = f.Lifecycle.F.Recipe(ingredient);
        f.Lifecycle.Recipes.Clear();
        f.Lifecycle.Recipes.Add(recipe);
        f.Lifecycle.F.Register(recipe);
        var stacks = new[] { "oak", "pine", "rotten", "birch" }
            .Select((wood, index) => f.Lifecycle.F.Stack(20 + index, "board-" + wood, 1)).ToArray();
        f.Lifecycle.World.SetupGet(value => value.Collectibles).Returns(stacks.Select(stack => stack.Collectible).ToList());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var tooltip = Assert.IsType<GuiElementRecipeBookTooltip>(menu.SingleComposer.GetElement("tooltip"));
        tooltip.Inspect(Assert.Single(session.Rows));
        var grid = Assert.Single(tooltip.Popup!.GetRichtext("recipe").Components
            .OfType<SlideshowGridRecipeTextComponent>());
        var choices = grid.GridRecipesAndUnnamedIngredients.SelectMany(entry =>
            entry.UnnamedIngredients != null ? entry.UnnamedIngredients.Values.SelectMany(value => value)
                : entry.Recipe!.ResolvedIngredients!.Where(value => value != null)
                    .Select(value => value!.ResolvedItemStack!)).Select(stack => stack.Collectible.Code.Path).ToHashSet();
        Assert.Equal(new[] { "board-oak", "board-pine" }, choices.Order().ToArray());
        Assert.Equal(EnumRecipeMatchType.Wildcard, ingredient.MatchingType);
        Assert.Equal("board-*", ingredient.Code!.Path);
    }

    private static void AssertContained(ElementBounds content, ElementBounds container)
    {
        Assert.True(content.absX >= container.absX - 0.001);
        Assert.True(content.absY >= container.absY - 0.001);
        Assert.True(content.absX + content.OuterWidth <= container.absX + container.OuterWidth + 0.001);
        Assert.True(content.absY + content.OuterHeight <= container.absY + container.OuterHeight + 0.001);
    }
}
