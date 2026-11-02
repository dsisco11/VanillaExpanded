using System.Diagnostics;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeBookPerformanceTestsTestSupport
{

internal sealed class NativeEqualityItem(int id) : VanillaExpanded.Tests.Mocks.MockItem(id)
    {
        internal int Comparisons { get; private set; }

        public override bool Equals(ItemStack thisStack, ItemStack otherStack, params string[] ignoreAttributeSubTrees)
        {
            Comparisons++;
            return thisStack.Class == otherStack.Class && thisStack.Id == otherStack.Id;
        }
    }

    internal sealed class BroadItem : Item
    {
        public override bool Satisfies(ItemStack thisStack, ItemStack otherStack) => true;
    }

}
