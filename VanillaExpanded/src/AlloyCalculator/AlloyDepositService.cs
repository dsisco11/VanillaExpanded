using System.Collections.Generic;
using System.Linq;

using VanillaExpanded.Network;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Plans and executes metal ingredient correction through the current alloy inventory policy.</summary>
internal static class AlloyDepositService
{
    #region Public API

    /// <summary>Plans and executes ingredient deposits against current inventory state.</summary>
    internal static AlloyDepositResultCode Execute(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        IReadOnlyList<MetalDepositIngredient> ingredients,
        IReadOnlyDictionary<int, ItemStack> calculatedStacks)
    {
        AlloyDepositResultCode result = CreatePlan(
            api,
            firepit,
            ingredients,
            calculatedStacks,
            out AlloyDepositPlan? plan);
        return result == AlloyDepositResultCode.Success
            ? ExecutePlan(api, firepit, plan!)
            : result;
    }

    /// <summary>Creates an ingredient plan using the current player inventory manager.</summary>
    internal static AlloyDepositResultCode CreatePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        IReadOnlyList<MetalDepositIngredient> ingredients,
        IReadOnlyDictionary<int, ItemStack> calculatedStacks,
        out AlloyDepositPlan? plan)
    {
        return CreatePlan(
            api,
            firepit,
            ingredients,
            calculatedStacks,
            api.World.Player.InventoryManager,
            out plan);
    }

    /// <summary>Creates and validates an ingredient plan against the supplied inventory manager.</summary>
    internal static AlloyDepositResultCode CreatePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        IReadOnlyList<MetalDepositIngredient> ingredients,
        IReadOnlyDictionary<int, ItemStack> calculatedStacks,
        IPlayerInventoryManager inventoryManager,
        out AlloyDepositPlan? plan)
    {
        plan = null;
        if (firepit.Inventory is not InventorySmelting inventory
            || !inventoryManager.OpenedInventories.Contains(inventory))
        {
            return AlloyDepositResultCode.InventoryClosed;
        }

        AlloyDepositPlan? createdPlan = AlloyDepositPlan.Create(
            ingredients,
            calculatedStacks,
            inventory.CookingSlots.Length);
        if (createdPlan is null)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        if (!AlloyTransferInventoryPolicy.TryCollect(api.World, inventoryManager, inventory, out IReadOnlyList<ItemSlot> externalSlots))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        // Existing cooking contents are counted separately; take locks only restrict external availability here.
        bool hasAllIngredients = ingredients.All(ingredient =>
            externalSlots.Where(AlloyTransferInventoryPolicy.CanWithdraw).Concat(inventory.CookingSlots)
                .Where(slot => !slot.Empty && SmeltsInto(api.World, slot.Itemstack, ingredient.ResolvedStack))
                .Sum(static slot => slot.StackSize)
            >= createdPlan.Targets.Where(target => target.Ingredient == ingredient).Sum(static target => target.Amount));
        if (!hasAllIngredients)
        {
            return AlloyDepositResultCode.InsufficientItems;
        }

        plan = createdPlan;
        return AlloyDepositResultCode.Success;
    }

    /// <summary>Executes ingredient correction using the current player inventory manager.</summary>
    internal static AlloyDepositResultCode ExecutePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        AlloyDepositPlan plan)
    {
        return ExecutePlan(api, firepit, plan, api.World.Player.InventoryManager);
    }

    /// <summary>Executes an ingredient plan through the supplied inventory manager using vanilla client packets.</summary>
    internal static AlloyDepositResultCode ExecutePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        AlloyDepositPlan plan,
        IPlayerInventoryManager inventoryManager)
    {
        if (firepit.Inventory is not InventorySmelting inventory
            || !inventoryManager.OpenedInventories.Contains(inventory))
        {
            return AlloyDepositResultCode.InventoryClosed;
        }

        if (!AlloyTransferInventoryPolicy.TryCollect(api.World, inventoryManager, inventory, out IReadOnlyList<ItemSlot> externalSlots)
            || plan.Targets.Any(target => target.SlotIndex < 0
                || target.SlotIndex >= inventory.CookingSlots.Length))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        // Recollect at execution instead of retaining a planning snapshot of opened containers.
        bool hasAllIngredients = plan.Targets
            .GroupBy(static target => target.Ingredient)
            .All(group => externalSlots.Where(AlloyTransferInventoryPolicy.CanWithdraw).Concat(inventory.CookingSlots)
                .Where(slot => !slot.Empty && SmeltsInto(api.World, slot.Itemstack, group.Key.ResolvedStack))
                .Sum(static slot => slot.StackSize)
                >= group.Sum(static target => target.Amount));
        if (!hasAllIngredients)
        {
            return AlloyDepositResultCode.InsufficientItems;
        }

        var retainedAmounts = new int[inventory.CookingSlots.Length];
        for (int slotIndex = 0; slotIndex < inventory.CookingSlots.Length; slotIndex++)
        {
            AlloyDepositSlotTarget? target = plan.Targets.FirstOrDefault(item => item.SlotIndex == slotIndex);
            InventorySlotCorrectionResult removeResult = ClientInventorySlotReconciler.RemoveIncorrectOrExcess(
                api,
                inventoryManager,
                inventory.CookingSlots[slotIndex],
                externalSlots,
                stack => target is not null && SmeltsInto(api.World, stack, target.Ingredient.ResolvedStack),
                target?.Amount ?? 0,
                out retainedAmounts[slotIndex],
                () => inventoryManager.OpenedInventories.Contains(inventory),
                slot => AlloyTransferInventoryPolicy.IsCurrentExternalSlot(api.World, inventoryManager, inventory, slot));
            if (removeResult != InventorySlotCorrectionResult.Success)
            {
                return MapResult(removeResult);
            }
        }

        foreach (AlloyDepositSlotTarget target in plan.Targets)
        {
            InventorySlotCorrectionResult addResult = ClientInventorySlotReconciler.AddMissing(
                api,
                inventoryManager,
                inventory.CookingSlots[target.SlotIndex],
                externalSlots,
                stack => SmeltsInto(api.World, stack, target.Ingredient.ResolvedStack),
                target.Amount - retainedAmounts[target.SlotIndex],
                () => inventoryManager.OpenedInventories.Contains(inventory),
                slot => AlloyTransferInventoryPolicy.IsCurrentExternalSlot(api.World, inventoryManager, inventory, slot));
            if (addResult != InventorySlotCorrectionResult.Success)
            {
                return MapResult(addResult);
            }
        }

        return AlloyDepositResultCode.Success;
    }

    #endregion

    #region Private

    /// <summary>Determines whether a source stack smelts into the desired metal ingredient.</summary>
    private static bool SmeltsInto(IWorldAccessor world, ItemStack source, ItemStack target)
    {
        ItemStack? smelted = source.Collectible
            .GetCombustibleProperties(world, source, null)?
            .SmeltedStack?
            .ResolvedItemstack;
        return target.Equals(world, smelted, GlobalConstants.IgnoredStackAttributes);
    }

    /// <summary>Maps a slot-correction result to the alloy calculator's user-facing result contract.</summary>
    private static AlloyDepositResultCode MapResult(InventorySlotCorrectionResult result)
    {
        return result switch
        {
            InventorySlotCorrectionResult.InventoryClosed => AlloyDepositResultCode.InventoryClosed,
            InventorySlotCorrectionResult.Success => AlloyDepositResultCode.Success,
            InventorySlotCorrectionResult.InsufficientSpace => AlloyDepositResultCode.InsufficientSpace,
            InventorySlotCorrectionResult.InsufficientItems => AlloyDepositResultCode.InsufficientItems,
            _ => AlloyDepositResultCode.TransferFailed
        };
    }

    #endregion
}
