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

internal static class CraftingTransferTestsTestSupport
{

internal sealed class ModificationCallbackItem(int id) : MockItem(id)
    {
        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null!) =>
            base.OnModifiedInInventorySlot(world, slot, extractedStack);
    }

    internal sealed class TransitionCallbackItem(int id) : MockItem(id)
    {
        public override TransitionState[] UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot slot) =>
            base.UpdateAndGetTransitionStates(world, slot);
    }

    internal sealed class NativeTransitionCallbackItem(int id) : MockItem(id)
    {
        protected override TransitionState[] UpdateAndGetTransitionStatesNative(IWorldAccessor world, ItemSlot slot) =>
            base.UpdateAndGetTransitionStatesNative(world, slot);
    }

    internal sealed class BehaviorCallbackItem(int id) : MockItem(id)
    {
        protected override CollectibleBehavior GetBehavior(CollectibleBehavior[] behaviors, Type type, bool withInheritance) =>
            base.GetBehavior(behaviors, type, withInheritance);
    }

    internal sealed class SetTemperatureCallbackItem(int id) : MockItem(id)
    {
        public override void SetTemperature(IWorldAccessor world, ItemStack stack, float temperature, bool delayCooldown = true) =>
            base.SetTemperature(world, stack, temperature, delayCooldown);
    }

    internal sealed class HasTemperatureCallbackItem(int id) : MockItem(id)
    {
        public override bool HasTemperature(IItemStack stack) =>
            base.HasTemperature(stack);
    }

}
