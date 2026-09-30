using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class AlloyDepositPlanTests
{
    [Fact]
    public void Create_AllocatesDesiredAmountsAcrossCookingSlots()
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

        AlloyDepositPlan? plan = AlloyDepositPlan.Create(ingredients, calculatedStacks, cookingSlotCount: 4);

        Assert.NotNull(plan);
        Assert.Equal([3, 3, 3, 1], plan.Targets.Select(static target => target.Amount));
        Assert.Equal([0, 1, 2, 3], plan.Targets.Select(static target => target.SlotIndex));
        Assert.Same(tin.Code, plan.Targets[^1].Ingredient.Code);
    }

    [Fact]
    public void Create_MissingCalculatedStack_ReturnsNull()
    {
        MockItem copper = CreateItem(1, "ingot-copper");
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 1, 1)
        ];

        AlloyDepositPlan? plan = AlloyDepositPlan.Create(
            ingredients,
            new Dictionary<int, ItemStack>(),
            cookingSlotCount: 4);

        Assert.Null(plan);
    }

    private static MockItem CreateItem(int id, string path)
    {
        MockItem item = MockItem.CreateNonLightSource(id);
        item.Code = new AssetLocation("game", path);
        return item;
    }
}