using System;
using System.Numerics;
using System.Runtime.CompilerServices;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.PerishableItemSlots;

/// <summary>Supplies cached freshness indicators for perishable items and meal containers.</summary>
internal sealed class FreshnessIndicatorProvider : IItemSlotIndicatorProvider
{
    private const long RefreshIntervalMilliseconds = 1_000;
    private const float StaleOpacityMultiplier = 0.75f;
    private const float FullyFreshFreshness = 0.75f;
    private readonly ConditionalWeakTable<ItemStack, FreshnessSample> samples = new();

    #region Public API
    /// <summary>Returns no indicator for disabled freshness, empty slots, or nonperishable stacks.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators) return false;

        ItemStack? stack = slot.Itemstack;
        ICoreAPI? api = slot.Inventory?.Api;
        if (stack is null || api is null) return false;

        FreshnessSample sample = samples.GetOrCreateValue(stack);
        long now = Environment.TickCount64;
        if (sample.LastUpdatedMilliseconds < 0
            || !ReferenceEquals(sample.Inventory, slot.Inventory)
            || now - sample.LastUpdatedMilliseconds >= RefreshIntervalMilliseconds)
        {
            TransitionState? state = ResolvePerishState(api.World, slot);
            sample.HasPerishState = state is not null;
            sample.Freshness = state is null ? 0 : CalculateFreshness(state);
            sample.Inventory = slot.Inventory;
            sample.LastUpdatedMilliseconds = now;
        }

        if (!sample.HasPerishState) return false;
        indicator = new ItemSlotIndicator(sample.Freshness, FreshnessColor(sample.Freshness));
        return true;
    }

    /// <summary>Calculates the remaining fraction of the item's fresh and transition lifetime.</summary>
    internal static float CalculateFreshness(TransitionState state)
    {
        float totalHours = state.FreshHours + state.TransitionHours;
        if (totalHours <= 0) return 0;

        float remainingTransitionHours = Math.Max(0, state.TransitionHours - Math.Max(0, state.TransitionedHours - state.FreshHours));
        return Math.Clamp((state.FreshHoursLeft + remainingTransitionHours) / totalHours, 0, 1);
    }

    /// <summary>Transitions from green through yellow and orange to a less opaque red as freshness falls.</summary>
    internal static Vector4 FreshnessColor(float freshness)
    {
        float amount = Math.Clamp(freshness / FullyFreshFreshness, 0, 1);
        Vector4 color = ColorUtilEx.MultiLerp(IndicatorColorPallette.FreshnessColors.AsSpan(), amount, smooth: true);
        float freshOpacity = Math.Clamp(VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity, 0, 1);
        return IndicatorColorPallette.WithOpacity(color, freshOpacity * float.Lerp(StaleOpacityMultiplier, 1, amount));
    }
    #endregion

    #region Private
    /// <summary>Resolves direct perish states or the first perishable meal content using inventory transition rates.</summary>
    private static TransitionState? ResolvePerishState(IWorldAccessor world, ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack!;
        TransitionState? state = stack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
        if (state is not null) return state;

        IBlockMealContainer? mealContainer = stack.Collectible.GetCollectibleInterface<IBlockMealContainer>();
        ItemStack[]? contents = mealContainer?.GetNonEmptyContents(world, stack);
        if (contents is null || contents.Length == 0 || slot.Inventory?.Api is not ICoreAPI api) return null;

        var dummyInventory = new DummyInventory(api);
        dummyInventory.OnAcquireTransitionSpeed += (type, contentStack, multiplier) =>
            type == EnumTransitionType.Perish
                ? slot.Inventory.GetTransitionSpeedMul(type, contentStack)
                : 0;

        ItemSlot contentSlot = BlockCrock.GetDummySlotForFirstPerishableStack(world, contents, null, dummyInventory);
        return contentSlot.Itemstack?.Collectible.UpdateAndGetTransitionState(world, contentSlot, EnumTransitionType.Perish);
    }

    /// <summary>Keeps each weakly owned stack sample tied to its inventory context.</summary>
    private sealed class FreshnessSample
    {
        internal long LastUpdatedMilliseconds { get; set; } = -1;
        internal InventoryBase? Inventory { get; set; }
        internal bool HasPerishState { get; set; }
        internal float Freshness { get; set; }
    }
    #endregion
}