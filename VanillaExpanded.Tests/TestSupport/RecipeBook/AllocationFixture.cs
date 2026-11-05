using Moq;
using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingAllocationTestsTestSupport;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal sealed class AllocationFixture
{
    internal CatalogueFixture Catalogue { get; } = new();
    internal ClientPlayer Player { get; } = CreatePlayer();
    internal Mock<IEventAPI> Events { get; } = new();
    internal Mock<ICoreAPI> Api { get; } = new();
    internal CraftingAllocator Allocator { get; }
    internal InventoryGeneric Hotbar { get; }
    internal InventoryGeneric Backpack { get; }
    internal InventoryGeneric Grid { get; }
    private int nextRecipe;

    internal AllocationFixture()
    {
        Api.Setup(api => api.Event).Returns(Events.Object);
        Api.Setup(api => api.World).Returns(Catalogue.World.Object);
        Events.Setup(events => events.TriggerMatchesRecipe(It.IsAny<IPlayer>(), It.IsAny<IRecipeBase>(),
            It.IsAny<ItemSlot[]>())).Returns(true);
        Events.Setup(events => events.TriggerMatchesRecipe(It.IsAny<IPlayer>(), It.IsAny<GridRecipe>(),
            It.IsAny<ItemSlot[]>(), It.IsAny<int>())).Returns(true);
        var data = (ClientWorldPlayerData)Player.WorldData;
        data.EntityPlayer = new EntityPlayer { Api = Api.Object };
        data.PlayerUID = "test";
        Hotbar = Inventory(12, "hotbar");
        Backpack = Inventory(8, "backpack");
        Grid = Inventory(10, "craftinggrid");
        var manager = (ClientPlayerInventoryManager)Player.InventoryManager;
        foreach (var inventory in new[] { Hotbar, Backpack, Grid })
            manager.Inventories.Add(inventory.InventoryID, inventory);
        Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe>());
        Allocator = new CraftingAllocator(Catalogue.World.Object, Player);
    }

    private static ClientPlayer CreatePlayer()
    {
        // IPlayer has an internal abstract member that Castle cannot implement. Use the native
        // implementation without starting ClientSettings or touching the user's client files.
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var data = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        var manager = new ClientPlayerInventoryManager(
            new Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>(), player, null!);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ClientPlayer).GetField("worlddata", fields)!.SetValue(player, data);
        typeof(ClientPlayer).GetField("inventoryMgr", fields)!.SetValue(player, manager);
        return player;
    }

    private InventoryGeneric Inventory(int count, string name)
    {
        var inventory = new InventoryGeneric(count, name, "test", null!);
        inventory.Api = Api.Object;
        inventory.AuditLogAccess = false;
        inventory.InvNetworkUtil = new Mock<IInventoryNetworkUtil>().Object;
        inventory.Open(Player);
        return inventory;
    }

    internal ItemStack Stack(int id, string name, int quantity, int maxStack = 64)
    {
        var stack = Catalogue.Stack(id, "game:" + name);
        stack.StackSize = quantity;
        stack.Collectible.MaxStackSize = maxStack;
        return stack;
    }

    internal CraftingRecipeIngredient Ingredient(ItemStack stack, int quantity = 1)
    {
        var resolved = CraftingStackSnapshot.Copy(stack);
        resolved.StackSize = quantity;
        return new() { Id = "I", Code = stack.Collectible.Code, Type = stack.Class,
            ResolvedItemStack = resolved, Quantity = quantity };
    }

    internal CraftingRecipeIngredient Wildcard(string code, int quantity = 1) => new() {
        Id = "choice", Code = new AssetLocation("game:" + code), Type = EnumItemClass.Item,
        MatchingType = EnumRecipeMatchType.Wildcard, Quantity = quantity
    };

    internal GridRecipe Recipe(params CraftingRecipeIngredient?[] ingredients)
    {
        var recipe = Catalogue.Recipe("allocation-" + nextRecipe++);
        recipe.Width = Math.Min(3, ingredients.Length);
        recipe.Height = (ingredients.Length + recipe.Width - 1) / recipe.Width;
        recipe.ResolvedIngredients = ingredients;
        return recipe;
    }

    internal void Register(params GridRecipe[] recipes)
    {
        Catalogue.Index.Clear();
        foreach (var recipe in recipes) Catalogue.Register(recipe);
    }

    internal CraftingInventorySnapshot Snapshot(params ItemStack[] stacks)
    {
        for (int i = 0; i < 10; i++) Hotbar[i].Itemstack = i < stacks.Length ? stacks[i] : null;
        return CraftingInventorySnapshot.Capture(Player, 3, 3, Environment.CurrentManagedThreadId, true);
    }

    internal CraftingAllocation Allocate(GridRecipe recipe, params ItemStack[] stacks)
    {
        Register(recipe);
        return AllocateRegistered(recipe, stacks);
    }

    internal CraftingAllocation AllocateRegistered(GridRecipe recipe, params ItemStack[] stacks) =>
        Allocator.Evaluate(Catalogue.Build(recipe).Recipes[0], Snapshot(stacks));

    internal void AssertConserved(CraftingAllocation allocation)
    {
        foreach (var placements in new[] { allocation.Ordinary, allocation.Shift })
        {
            foreach (var placement in placements)
            {
                Assert.InRange(placement.Quantity, 1, placement.GetStack().Collectible.MaxStackSize);
                Assert.Equal(placement.Quantity, placement.Sources.Sum(source => source.Quantity));
            }
            Assert.Equal(placements.Length, placements.Select(placement => placement.GridPosition).Distinct().Count());
            foreach (var group in placements.SelectMany(placement => placement.Sources).GroupBy(source => source.Slot))
            {
                var inventory = new[] { Hotbar, Backpack, Grid }.First(inv => inv.InventoryID == group.Key.InventoryId);
                Assert.True(group.Sum(source => source.Quantity) <= inventory[group.Key.Index]!.StackSize);
            }
        }
    }
}
