using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookAllocationTooltipWarningsTests
{
    [Fact]
    public void TooltipWarningsDoNotRepeatIngredientsDeficitsOrControls()
    {
        var f = new AllocationFixture();
        var wildcard = f.Wildcard("board-*", 4);
        wildcard.AllowedVariants = new[] { "oak", "pine" };
        wildcard.SkipVariants = new[] { "rotten" };
        var stick = f.Stack(2, "stick", 1);
        var recipe = f.Recipe(wildcard, f.Ingredient(stick, 2));
        f.Register(recipe);
        f.Snapshot(f.Stack(1, "board-oak", 3), stick);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        string text = RecipeBookTooltip.Warnings(row, key => key);
        Assert.Equal(2, row.Evaluation.Missing.Length);
        Assert.Empty(text);
        Assert.DoesNotContain("recipebook-tool", RecipeBookTooltip.Warnings(WithTool(f), key => key));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TaggedIngredientsKeepNativeMatchingWithoutDumpingTagExpressionsIntoTooltip(bool disjunctive)
    {
        var f = new AllocationFixture();
        var first = Tag(1, 2);
        var second = Tag(3);
        var forbidden = Tag(4);
        var ingredient = f.Wildcard("*", 2);
        ingredient.Tags = new ComplexTagCondition<TagSet>
        {
            isDisjunctive = disjunctive,
            conditions = new[]
            {
                new ComplexTagCondition<TagSet>.Condition { RequiredTags = first, ForbiddenTags = forbidden },
                new ComplexTagCondition<TagSet>.Condition { RequiredTags = second }
            }
        };
        Assert.True(ingredient.Tags.Matches(disjunctive ? Tag(1, 2) : Tag(1, 3)));
        Assert.False(ingredient.Tags.Matches(disjunctive ? Tag(1) : Tag(1, 2)));
        Assert.False(ingredient.Tags.Matches(disjunctive ? Tag(1, 2, 4) : Tag(1, 3, 4)));
        var recipe = f.Recipe(ingredient);
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        string text = RecipeBookTooltip.Warnings(row, key => key);
        Assert.Empty(text);
        Assert.Equal(2, Assert.Single(row.Evaluation.Missing).Quantity);
        Assert.Equal(new CraftingIngredientSummary("Matching ingredient", 0, 2),
            Assert.Single(RecipeBookTooltip.IngredientSummary(row, _ => "Matching ingredient")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyRequiredTagChoiceDoesNotAddTagDiagnosticsToTooltip(bool disjunctive)
    {
        var f = new AllocationFixture();
        var ingredient = f.Wildcard("*");
        ingredient.Tags = new ComplexTagCondition<TagSet>
        {
            isDisjunctive = disjunctive,
            conditions = new[] { new ComplexTagCondition<TagSet>.Condition { ForbiddenTags = Tag(1) } }
        };
        var recipe = f.Recipe(ingredient);
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        string text = RecipeBookTooltip.Warnings(Assert.Single(session.Rows), key => key);
        Assert.Empty(text);
    }
}
