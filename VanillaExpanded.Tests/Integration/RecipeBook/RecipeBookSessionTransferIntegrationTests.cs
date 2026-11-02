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
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookSessionTransferIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingCraftableOnlyRestoresPartialPlacementThroughNativeCoordinator(bool shift)
    {
        using var f = new TransferFixture(2, 1);
        var output = f.F.Stack(99, "output", 7);
        f.F.Grid[9].Itemstack = output;
        using var session = new RecipeBookSession(f.F.Catalogue.Build(f.Entry.Recipe),
            f.F.Allocator, f.Snapshot, f.Coordinator.Execute);
        session.Refresh();
        var partial = Assert.Single(session.Rows);
        Assert.False(session.CraftableOnly.Enabled);
        Assert.True(partial.CanPlace);
        Assert.False(partial.Evaluation.CanCraft);
        Assert.Equal(1, partial.Evaluation.Allocated);
        session.CraftableOnly.SetEnabled(true);
        Assert.Empty(session.Rows);
        session.CraftableOnly.SetEnabled(false);
        Assert.Same(partial.Allocation, Assert.Single(session.Rows).Allocation);
        var result = session.Activate(partial.Output.Id, shift);
        Assert.Equal(CraftingDispatchStatus.Dispatched, result.Status);
        Assert.Equal(1, f.F.Grid[0].StackSize);
        Assert.Null(f.F.Hotbar[0].Itemstack);
        var packet = Assert.IsType<Packet_Client>(Assert.Single(f.Packets));
        Assert.Equal(1, packet.MoveItemstack.Quantity);
        Assert.Same(output, f.F.Grid[9].Itemstack);
        Assert.Equal(7, output.StackSize);
        Assert.Empty(f.Notifications);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 20)]
    public void SessionUsesRealAllocationAndCoordinatorForBothInputs(bool shift, int expected)
    {
        using var f = new TransferFixture(2, 20);
        var catalogue = f.F.Catalogue.Build(f.Entry.Recipe);
        var session = new RecipeBookSession(catalogue, f.F.Allocator,
            () => CraftingInventorySnapshot.Capture(f.F.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            f.Coordinator.Execute);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.True(row.Evaluation.CanCraft);
        Assert.Equal(2, Assert.Single(row.Allocation!.Ordinary).Quantity);
        var result = session.Activate(row.Output.Id, shift);
        Assert.Equal(CraftingDispatchStatus.Dispatched, result.Status);
        Assert.Equal(expected, f.F.Grid[0].StackSize);
        Assert.Single(f.Packets);
        session.Refresh();
        Assert.Equal(2, Assert.Single(session.Rows).Evaluation.Allocated);
        Assert.True(f.F.Grid[9].Empty);
    }

    [Fact]
    public void DeferredOrderingKeepsShownRecipeButUpdatesItsDeficitsAndRevalidatesClick()
    {
        using var f = new TransferFixture(2, 2);
        var a = f.Entry.Recipe;
        var other = f.F.Stack(2, "other", 2);
        var b = f.F.Recipe(f.F.Ingredient(other, 2));
        f.F.Register(a, b);
        var catalogue = f.F.Catalogue.Build(a, b);
        var session = new RecipeBookSession(catalogue, f.F.Allocator,
            () => CraftingInventorySnapshot.Capture(f.F.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            f.Coordinator.Execute);
        session.Refresh();
        int first = session.Rows[0].Output.Id;
        session.Interact(first);
        f.F.Hotbar[0].Itemstack = other;
        session.Refresh();
        Assert.Equal(first, session.Rows[0].Output.Id);
        Assert.False(session.Rows[0].Evaluation.CanCraft);
        Assert.Equal(2, Assert.Single(session.Rows[0].Evaluation.Missing).Quantity);
        var result = session.Activate(first, false);
        Assert.Equal(CraftingDispatchStatus.Rejected, result.Status);
        Assert.Equal(CraftingTransferPlanner.Empty, result.Reason);
        Assert.Empty(f.Packets);
        session.Interact(null);
        Assert.NotEqual(first, session.Rows[0].Output.Id);
        Assert.True(session.Rows[0].Evaluation.CanCraft);
    }

    [Theory]
    [InlineData("same-code", false, false)]
    [InlineData("same-code", false, true)]
    [InlineData("same-code", true, false)]
    [InlineData("same-code", true, true)]
    [InlineData("wildcard", false, false)]
    [InlineData("wildcard", false, true)]
    [InlineData("wildcard", true, false)]
    [InlineData("wildcard", true, true)]
    [InlineData("custom", false, false)]
    [InlineData("custom", false, true)]
    [InlineData("custom", true, false)]
    [InlineData("custom", true, true)]
    public void ProductionCatalogueAllowsNativePlacementDespitePotentialCompetition(
        string competition, bool partial, bool shift)
    {
        using var f = new TransferFixture(2, partial ? 1 : 64);
        var selected = f.Entry.Recipe;
        var other = f.F.Recipe(competition == "wildcard" ? f.F.Wildcard("*") : f.F.Ingredient(f.Ingredient(1)));
        other.Shapeless = competition == "wildcard";
        if (competition == "custom")
            other = new CustomGridRecipe {
                Name = other.Name, Width = 1, Height = 1, Output = other.Output,
                ResolvedIngredients = new[] { f.F.Ingredient(f.F.Stack(999, "unrelated", 1)) }
            };
        foreach (var recipe in new[] { selected, other })
        {
            var output = recipe.Output!.ResolvedItemStack!;
            recipe.Output.ResolvedItemStack = new ItemStack(new NamedOutputItem(output.Id) {
                Code = output.Collectible.Code
            });
        }
        f.F.Register(selected, other);

        var api = new Mock<ICoreClientAPI>();
        var world = new Mock<IClientWorldAccessor>();
        var loader = new Mock<IModLoader>();
        loader.Setup(mods => mods.GetModSystem<CharacterSystem>(true)).Returns(new CharacterSystem());
        api.SetupGet(value => value.World).Returns(world.Object);
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        world.SetupGet(value => value.Player).Returns(f.F.Player);
        world.SetupGet(value => value.GridRecipes).Returns(new List<GridRecipe> { selected, other });
        world.SetupGet(value => value.FastSearchRecipesByIngredient).Returns(f.F.Catalogue.Index);
        world.SetupGet(value => value.Calendar).Returns(Mock.Of<IClientGameCalendar>());
        var (catalogue, allocator) = RecipeBookSystem.BuildCatalogue(api.Object);
        using var coordinator = new CraftingTransferCoordinator(f.F.Player, world.Object, allocator,
            f.Packets.Add, f.Notifications.Add, f.RefreshQueue.Enqueue, Environment.CurrentManagedThreadId, true);
        var session = new RecipeBookSession(catalogue, allocator, f.Snapshot, coordinator.Execute);
        session.Refresh();
        var row = Assert.Single(session.Rows.Where(row => ReferenceEquals(row.Recipe.Recipe, selected)));
        Assert.True(row.Evaluation.IsSupported);
        Assert.Equal(!partial, row.Evaluation.CanCraft);
        Assert.True(row.CanPlace);
        Assert.Equal(partial ? 1 : 0, row.Evaluation.MissingUnits);
        Assert.Equal(CraftingDispatchStatus.Dispatched, session.Activate(row.Output.Id, shift).Status);
        Assert.Equal(partial ? 1 : shift ? 64 : 2, f.F.Grid[0].StackSize);
        Assert.True(f.F.Grid[9].Empty);
        Assert.NotEmpty(f.Packets);
        Assert.Empty(f.Notifications);
    }
}
