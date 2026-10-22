using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

internal sealed class QuernPolicy : AutoStashPolicy
{
    private readonly IWorldAccessor world;
    private readonly IInventory inventory;
    private readonly CollectibleObject? inputType;

    public QuernPolicy(IWorldAccessor world, IInventory inventory)
    {
        this.world = world;
        this.inventory = inventory;
        ItemStack? input = inventory[0]?.Itemstack;
        if (input is not null && input.Collectible.GetGrindingProperties(world, input) is not null)
            inputType = input.Collectible;
    }

    public override IEnumerable<AutoStashSourcePass> GetSourcePasses(IInventory? backpack, IInventory? hotbar)
    {
        if (backpack is not null) yield return new AutoStashSourcePass(backpack, 0);
        if (hotbar is not null) yield return new AutoStashSourcePass(hotbar, 0);
    }

    public override bool IsEligible(ItemStack stack, AutoStashSourcePass pass)
        => inputType is not null && ReferenceEquals(stack.Collectible, inputType)
            && stack.Collectible.GetGrindingProperties(world, stack) is not null;

    public override bool CanStartInteraction(IPlayerInventoryManager owner, BlockSelection? selection = null,
        IPlayer? player = null, bool forHelp = false, AutoStashAssessment? assessment = null)
        => (selection is null || selection.SelectionBoxIndex == 0)
            && owner.OpenedInventories?.Contains(inventory) != true;

    public override int GetQuantity(ItemSlot source, AutoStashSourcePass pass)
    {
        ItemSlot? input = inventory[0];
        ItemStack? stack = source.Itemstack;
        ItemStack? current = input?.Itemstack;
        if (input is null || stack is null || current is null || !ReferenceEquals(current.Collectible, inputType)
            || !IsEligible(stack, pass) || !source.CanTake()
            || !input.CanTakeFrom(source, EnumMergePriority.AutoMerge))
            return 0;

        return Math.Max(0, Math.Min(source.StackSize, Math.Min(input.GetRemainingSlotSpace(stack),
            current.Collectible.GetMergableQuantity(current, stack, EnumMergePriority.AutoMerge))));
    }
}
