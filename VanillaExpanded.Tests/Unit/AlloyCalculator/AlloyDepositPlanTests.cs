using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class AlloyDepositPlanTests
{
    [Fact]
    public void CreateRequest_CapturesTotalsWithoutSpreadingAcrossCookingSlots()
    {
        MockItem copper = CreateItem(1, "ingot-copper");
        MockItem tin = CreateItem(2, "ingot-tin");
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 0.88f, 0.92f),
            new(tin.Code, new ItemStack(tin), 0.08f, 0.12f)
        ];
        Dictionary<int, ItemStack> calculatedStacks = new()
        {
            [0] = new ItemStack(copper, 9),
            [1] = new ItemStack(tin, 1)
        };

        AlloyDepositPlan? plan = AlloyDepositPlan.CreateRequest(ingredients, calculatedStacks, cookingSlotCount: 4);

        Assert.NotNull(plan);
        Assert.Equal([9, 1], plan.Targets.Select(static target => target.Amount));
        Assert.All(plan.Targets, static target => Assert.Null(target.DesiredStack));
        Assert.Same(tin.Code, plan.Targets[^1].Ingredient.Code);
    }

    [Fact]
    public void CreateRequest_MissingCalculatedStack_ReturnsNull()
    {
        MockItem copper = CreateItem(1, "ingot-copper");
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 1, 1)
        ];

        AlloyDepositPlan? plan = AlloyDepositPlan.CreateRequest(
            ingredients,
            new Dictionary<int, ItemStack>(),
            cookingSlotCount: 4);

        Assert.Null(plan);
    }

    [Theory]
    [InlineData(18, 64, 9)]
    [InlineData(120, 64, 60)]
    [InlineData(18, 10, 9)]
    public void Resolve_ReservesAllFormsBeforeBalancing(int copperAmount, int copperStackLimit, int expectedCopperPerSlot)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem copper = CreateItem(1, "ingot-copper");
        MockItem tin = CreateItem(2, "ingot-tin");
        MockItem copperNugget = CreateSmeltableItem(3, "nugget-copper", copper);
        copperNugget.MaxStackSize = copperStackLimit;
        MockItem tinBit = CreateSmeltableItem(4, "metalbit-tin", tin);
        MockItem tinNugget = CreateSmeltableItem(5, "nugget-tin", tin);
        var supplies = new InventoryGeneric(20, "supply", "planner", null!) { Api = fixture.Api };
        int remaining = copperAmount;
        int slotIndex = 0;
        while (remaining > 0)
        {
            int amount = Math.Min(remaining, copperStackLimit);
            supplies[slotIndex++].Itemstack = new ItemStack(copperNugget, amount);
            remaining -= amount;
        }
        supplies[slotIndex++].Itemstack = new ItemStack(tinBit, 1);
        supplies[slotIndex].Itemstack = new ItemStack(tinNugget, 1);
        var cooking = new InventoryGeneric(4, "cooking", "planner", null!) { Api = fixture.Api };
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 0.9f, 0.9f),
            new(tin.Code, new ItemStack(tin), 0.1f, 0.1f)
        ];
        AlloyDepositPlan request = Assert.IsType<AlloyDepositPlan>(AlloyDepositPlan.CreateRequest(
            ingredients, new Dictionary<int, ItemStack>
            {
                [0] = new(copper, copperAmount),
                [1] = new(tin, 2)
            }, cooking.Count));

        AlloyDepositPlan plan = Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true));

        Assert.Equal([0, 1, 2, 3], plan.Targets.Select(target => target.SlotIndex));
        Assert.Equal([expectedCopperPerSlot, expectedCopperPerSlot, 1, 1], plan.Targets.Select(target => target.Amount));
        Assert.Equal([copperNugget.Code, copperNugget.Code, tinBit.Code, tinNugget.Code],
            plan.Targets.Select(target => target.DesiredStack!.Collectible.Code));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
        Assert.Equal(copperAmount + 2, supplies.Sum(slot => slot.StackSize));
        Assert.Equal(plan, AlloyDepositStackPlanner.Resolve(
            fixture.World, plan, cooking.ToArray(), supplies, MatchesIngredient));

        var previousLayout = new AlloyDepositPlan(
        [
            new(0, ingredients[0], copperAmount / 3, new ItemStack(copperNugget)),
            new(1, ingredients[0], copperAmount / 3, new ItemStack(copperNugget)),
            new(2, ingredients[0], copperAmount / 3, new ItemStack(copperNugget)),
            new(3, ingredients[1], 2, new ItemStack(tinNugget))
        ]);
        AlloyDepositPlan replanned = Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, previousLayout, cooking.ToArray(), supplies, MatchesIngredient));
        Assert.Equal(plan.Targets.Select(target => (target.SlotIndex, target.Amount, target.DesiredStack!.Collectible.Code)),
            replanned.Targets.Select(target => (target.SlotIndex, target.Amount, target.DesiredStack!.Collectible.Code)));
    }

    [Fact]
    public void Resolve_RequiredFormsExceedTotalSlots_ReturnsNullWithoutChangingSupply()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-copper");
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var supplies = new InventoryGeneric(5, "supply", "planner", null!) { Api = fixture.Api };
        for (int index = 0; index < supplies.Count; index++)
            supplies[index].Itemstack = new ItemStack(CreateSmeltableItem(index + 2, $"copper-form-{index}", metal), 1);
        var cooking = new InventoryGeneric(4, "cooking", "planner", null!) { Api = fixture.Api };
        var request = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, ingredient, 5)]);

        Assert.Null(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
        Assert.All(supplies, slot => Assert.Equal(1, slot.StackSize));
    }

    private static bool MatchesIngredient(ItemStack stack, MetalDepositIngredient ingredient)
    {
        return stack.Collectible.CombustibleProps?.SmeltedStack?.Code == ingredient.Code;
    }

    private static MockItem CreateSmeltableItem(int id, string path, MockItem ingot)
    {
        MockItem item = CreateItem(id, path);
        item.MaxStackSize = 64;
        item.CombustibleProps = new CombustibleProperties
        {
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack { Code = ingot.Code, ResolvedItemstack = new ItemStack(ingot) }
        };
        return item;
    }

    private static MockItem CreateItem(int id, string path)
    {
        MockItem item = MockItem.CreateNonLightSource(id);
        item.Code = new AssetLocation("game", path);
        return item;
    }
}