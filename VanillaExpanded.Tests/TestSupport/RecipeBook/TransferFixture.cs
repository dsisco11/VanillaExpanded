using System.Collections.Immutable;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal sealed class TransferFixture : IDisposable
{
    internal readonly AllocationFixture F = new();
    internal readonly List<object> Packets = new();
    internal readonly List<CraftingDispatchResult> Notifications = new();
    internal readonly CraftingRecipeEntry Entry;
    internal Action<object>? OnSend;
    internal int Refreshes;
    internal readonly Queue<Action> RefreshQueue = new();
    internal readonly CraftingTransferCoordinator Coordinator;
    internal readonly Mock<IClientNetworkAPI> Network = new();
    private readonly ItemStack ingredient;

    internal TransferFixture(int demand, int stock, int positions = 1)
    {
        ingredient = F.Stack(1, "board", 1);
        var recipe = F.Recipe(Enumerable.Range(0, positions).Select(_ => F.Ingredient(ingredient, demand)).ToArray());
        F.Register(recipe);
        F.Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe> { recipe });
        F.Catalogue.World.Setup(world => world.Calendar).Returns(Mock.Of<IGameCalendar>());
        Entry = F.Catalogue.Build(recipe).Recipes[0];
        if (stock > 0) F.Hotbar[0].Itemstack = Ingredient(stock);
        Network.Setup(network => network.SendPacketClient(It.IsAny<object>())).Callback<object>(packet =>
        {
            Packets.Add(packet);
            OnSend?.Invoke(packet);
        });
        Coordinator = new(F.Player, F.Catalogue.World.Object, F.Allocator,
            Network.Object.SendPacketClient, Notifications.Add, RefreshQueue.Enqueue, Environment.CurrentManagedThreadId, true);
        Coordinator.InventoryChanged += () => Refreshes++;
    }

    internal ItemStack Ingredient(int count)
    {
        var result = CraftingStackSnapshot.Copy(ingredient);
        result.StackSize = count;
        return result;
    }

    internal CraftingInventorySnapshot Snapshot() =>
        CraftingInventorySnapshot.Capture(F.Player, 3, 3, Environment.CurrentManagedThreadId, true);
    internal CraftingTransferState State() => new(F.Player, F.Catalogue.World.Object, Snapshot(), true);
    internal void Refresh()
    {
        while (RefreshQueue.TryDequeue(out var refresh)) refresh();
    }
    internal void FillStorage(bool skipFirst = false)
    {
        for (int i = skipFirst ? 1 : 0; i < 10; i++) F.Hotbar[i].Itemstack = F.Stack(100 + i, "filled-" + i, 64);
        for (int i = 4; i < F.Backpack.Count; i++) F.Backpack[i].Itemstack = F.Stack(200 + i, "filled-" + i, 64);
    }

    internal ItemStack?[] CopyAll() => new[] { F.Hotbar, F.Backpack, F.Grid }.SelectMany(inv =>
        Enumerable.Range(0, inv.Count).Select(i => CraftingTransferState.Copy(inv[i].Itemstack))).ToArray();

    internal void AssertSame(ItemStack?[] before)
    {
        var after = CopyAll();
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
            Assert.True(CraftingTransferState.Same(F.Catalogue.World.Object, before[i], after[i]));
    }
    public void Dispose() => Coordinator.Dispose();
}
