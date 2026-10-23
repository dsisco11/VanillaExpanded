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
        LogSupplyDiagnostics(api, firepit, ingredients, calculatedStacks);
        AlloyDepositResultCode result = CreatePlan(
            api,
            firepit,
            ingredients,
            calculatedStacks,
            out AlloyDepositPlan? plan);
#if DEBUG
        api.Logger.Notification($"[VanillaExpanded] Alloy deposit diagnostics: planning result={result}.");
#endif
        if (result != AlloyDepositResultCode.Success) return result;

#if DEBUG
        foreach (AlloyDepositSlotTarget target in plan!.Targets)
        {
            api.Logger.Notification($"[VanillaExpanded] Alloy deposit diagnostics: target slot={target.SlotIndex}, ingredient={target.Ingredient.Code}, form={target.DesiredStack?.Collectible.Code}, amount={target.Amount}.");
        }
#endif
        result = ExecutePlan(api, firepit, plan!);
#if DEBUG
        api.Logger.Notification($"[VanillaExpanded] Alloy deposit diagnostics: execution result={result}.");
#endif
        return result;
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

        AlloyDepositPlan? createdPlan = AlloyDepositPlan.CreateRequest(
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

        ItemSlot[] supplies = CollectCompatibleSupplies(externalSlots, inventory);
        // Existing cooking contents are counted separately; take locks only restrict external availability here.
        bool hasAllIngredients = ingredients.All(ingredient =>
            supplies
                .Where(slot => !slot.Empty && SmeltsInto(api.World, slot.Itemstack, ingredient.ResolvedStack))
                .Sum(static slot => slot.StackSize)
            >= createdPlan.Targets.Where(target => target.Ingredient == ingredient).Sum(static target => target.Amount));
        if (!hasAllIngredients)
        {
            return AlloyDepositResultCode.InsufficientItems;
        }

        plan = AlloyDepositStackPlanner.Resolve(api.World, createdPlan, inventory.CookingSlots,
            supplies,
            (stack, ingredient) => SmeltsInto(api.World, stack, ingredient.ResolvedStack),
            spreadAcrossCookingSlots: true);
        if (plan is null) return AlloyDepositResultCode.InsufficientSpace;
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

        ItemSlot[] supplies = CollectCompatibleSupplies(externalSlots, inventory);
        // Recollect at execution instead of retaining a planning snapshot of opened containers.
        bool hasAllIngredients = plan.Targets
            .GroupBy(static target => target.Ingredient)
            .All(group => supplies
                .Where(slot => !slot.Empty && SmeltsInto(api.World, slot.Itemstack, group.Key.ResolvedStack))
                .Sum(static slot => slot.StackSize)
                >= group.Sum(static target => target.Amount));
        if (!hasAllIngredients)
        {
            return AlloyDepositResultCode.InsufficientItems;
        }

        // Resolve again from live supplies so every slot receives a single merge-compatible form before any movement.
        AlloyDepositPlan? resolvedPlan = AlloyDepositStackPlanner.Resolve(api.World, plan, inventory.CookingSlots,
            supplies,
            (stack, ingredient) => SmeltsInto(api.World, stack, ingredient.ResolvedStack));
        if (resolvedPlan is null) return AlloyDepositResultCode.InsufficientSpace;
        plan = resolvedPlan;

        var retainedAmounts = new int[inventory.CookingSlots.Length];
        for (int slotIndex = 0; slotIndex < inventory.CookingSlots.Length; slotIndex++)
        {
            AlloyDepositSlotTarget? target = plan.Targets.FirstOrDefault(item => item.SlotIndex == slotIndex);
            InventorySlotCorrectionResult removeResult = ClientInventorySlotReconciler.RemoveIncorrectOrExcess(
                api,
                inventoryManager,
                inventory.CookingSlots[slotIndex],
                externalSlots,
                stack => target is not null && MatchesTarget(api.World, stack, target),
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
                stack => MatchesTarget(api.World, stack, target),
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

    private static ItemSlot[] CollectCompatibleSupplies(
        IReadOnlyList<ItemSlot> externalSlots,
        InventorySmelting inventory)
    {
        // Cooking slots share container restrictions. CanHold ignores current contents,
        // which may be removed by the plan, unlike CanTakeFrom's merge/capacity checks.
        return externalSlots.Where(AlloyTransferInventoryPolicy.CanWithdraw)
            .Concat(inventory.CookingSlots)
            .Where(source => !source.Empty && inventory.CookingSlots.All(target => target.CanHold(source)))
            .ToArray();
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogSupplyDiagnostics(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        IReadOnlyList<MetalDepositIngredient> ingredients,
        IReadOnlyDictionary<int, ItemStack> calculatedStacks)
    {
        var manager = api.World.Player.InventoryManager;
        AlloyTransferInventoryPolicy.TryCollect(api.World, manager, firepit.Inventory, out var externalSlots,
            message => api.Logger.Notification($"[VanillaExpanded] Alloy deposit diagnostics: {message}"));
        var inventories = new HashSet<IInventory>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        IInventory? backpack = manager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbar = manager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (backpack is not null) inventories.Add(backpack);
        if (hotbar is not null) inventories.Add(hotbar);
        foreach (IInventory inventory in manager.OpenedInventories) inventories.Add(inventory);

        for (int index = 0; index < ingredients.Count; index++)
        {
            MetalDepositIngredient ingredient = ingredients[index];
            api.Logger.Notification($"[VanillaExpanded] Alloy deposit diagnostics: requested {ingredient.Code}={calculatedStacks.GetValueOrDefault(index)?.StackSize ?? 0}.");
            foreach (IInventory inventory in inventories)
            {
                foreach (ItemSlot slot in inventory)
                {
                    if (slot.Empty || !SmeltsInto(api.World, slot.Itemstack, ingredient.ResolvedStack)) continue;
                    api.Logger.Notification($"[VanillaExpanded] Alloy deposit diagnostics: supply inventory={inventory.InventoryID}, item={slot.Itemstack.Collectible.Code}, amount={slot.StackSize}, externalEligible={externalSlots.Contains(slot)}, canWithdraw={AlloyTransferInventoryPolicy.CanWithdraw(slot)}.");
                }
            }
        }
    }

    /// <summary>Requires both the desired metal output and the concrete stack form selected for this slot.</summary>
    private static bool MatchesTarget(IWorldAccessor world, ItemStack stack, AlloyDepositSlotTarget target)
    {
        return SmeltsInto(world, stack, target.Ingredient.ResolvedStack)
            && (target.DesiredStack is null || target.DesiredStack.Equals(world, stack, GlobalConstants.IgnoredStackAttributes));
    }

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
