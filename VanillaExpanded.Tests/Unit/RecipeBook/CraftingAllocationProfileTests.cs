using System.Diagnostics;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookPerformanceTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingAllocationProfileTests(ITestOutputHelper output)
{
    [Fact]
    public void CatalogueRefreshProfile()
    {
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(0, 2000).Select(id =>
        {
            var ingredient = f.Ingredient(f.Stack(id < 100 ? 1 : id + 2, id < 100 ? "board" : "other-" + id, 1));
            return f.Recipe(Enumerable.Repeat(ingredient, 9).ToArray());
        }).ToArray();
        f.Register(recipes);
        var catalogue = f.Catalogue.Build(recipes);
        var snapshot = f.Snapshot(f.Stack(1, "board", 64));
        var timer = Stopwatch.StartNew();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var results = f.Allocator.EvaluateCatalogue(catalogue, snapshot);
        timer.Stop();
        output.WriteLine($"2000 recipes, 9 positions, 100 attention candidates: {timer.Elapsed.TotalMilliseconds:F1} ms, "
            + $"{(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024d / 1024:F2} MiB");
        Assert.Equal(2000, results.Count);
    }

    [Fact]
    public void RepeatedWildcardAllocationProfile()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(Enumerable.Range(0, 9).Select(_ => f.Wildcard("board-*")).ToArray());
        var stock = Enumerable.Range(0, 8).Select(id => f.Stack(id + 1, "board-" + id, 64)).ToArray();
        f.Register(recipe);
        var entry = f.Catalogue.Build(recipe).Recipes[0];
        var snapshot = f.Snapshot(stock);
        var timer = Stopwatch.StartNew();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var result = f.Allocator.Evaluate(entry, snapshot);
        output.WriteLine($"9 repeated wildcard positions, 8 incompatible variants: {timer.Elapsed.TotalMilliseconds:F1} ms, "
            + $"{(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024d / 1024:F2} MiB");
        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(32, result.CompleteExecutions);
        Assert.Equal(512, result.Shift.Sum(placement => placement.Quantity));
    }
}
