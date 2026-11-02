using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeBookTooltipSummaryTestsTestSupport
{

internal static CraftingRecipeSelection Selection(GridRecipe recipe, CraftingAllocation allocation)
    {
        var entry = new CraftingRecipeEntry(0, recipe, "Output");
        return new(new(0, [entry]), entry, allocation.Evaluation, false, allocation);
    }

    internal static ItemStack NamedStack(AllocationFixture f, int id, string name, int quantity, int maxStack = 64)
    {
        var stack = f.Stack(id, name, quantity, maxStack);
        var item = new Mock<MockItem>(id, (byte)0, f.Api.Object) { CallBase = true };
        item.Object.Code = stack.Item.Code;
        item.Object.MaxStackSize = maxStack;
        item.Setup(value => value.GetHeldItemName(It.IsAny<ItemStack>())).Returns(name);
        return new(item.Object, quantity);
    }

}
