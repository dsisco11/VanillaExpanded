using Moq;
using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class CraftingAllocationTestsTestSupport
{

internal sealed class CustomIngredient : CraftingRecipeIngredient;

    internal sealed class CustomDamageItem(int id) : MockItem(id)
    {
        public override void DamageItem(IWorldAccessor world, Vintagestory.API.Common.Entities.Entity byEntity,
            ItemSlot itemSlot, int amount = 1, bool destroyOnZeroDurability = true) =>
            throw new InvalidOperationException("Allocation must not invoke a custom durability mutation.");
    }

}
