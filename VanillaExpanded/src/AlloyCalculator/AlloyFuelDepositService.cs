using System.Collections.Generic;
using System.Linq;

using VanillaExpanded.Network;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Plans and applies fuel inventory corrections for the metals in a firepit.</summary>
internal static class AlloyFuelDepositService
{
    #region Public API

    /// <summary>Plans and executes fuel correction against current inventory state.</summary>
    internal static AlloyDepositResultCode Execute(
        ICoreClientAPI api,
        BlockEntityFirepit firepit)
    {
        AlloyDepositResultCode result = CreatePlan(api, firepit, out AlloyFuelDepositPlan? plan);
        return result == AlloyDepositResultCode.Success
            ? ExecutePlan(api, firepit, plan!)
            : result;
    }

    /// <summary>Creates a fuel plan using the current player inventory manager.</summary>
    internal static AlloyDepositResultCode CreatePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        out AlloyFuelDepositPlan? plan)
    {
        return CreatePlan(api, firepit, api.World.Player.InventoryManager, out plan);
    }

    /// <summary>Creates and validates a minimal-fuel plan against the supplied inventory manager.</summary>
    internal static AlloyDepositResultCode CreatePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        IPlayerInventoryManager playerInventory,
        out AlloyFuelDepositPlan? plan)
    {
        plan = null;
        IWorldAccessor world = api.World;
        if (firepit.Inventory is not InventorySmelting inventory
            || !playerInventory.OpenedInventories.Contains(inventory))
        {
            return AlloyDepositResultCode.InventoryClosed;
        }

        if (firepit.inputStack is null)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        if (!AlloyTransferInventoryPolicy.TryCollect(world, playerInventory, inventory, out IReadOnlyList<ItemSlot> externalSlots))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        ItemStack inputStack = firepit.inputStack;
        float meltingPoint = inputStack.Collectible.GetMeltingPoint(world, inventory, firepit.inputSlot);
        float meltingDuration = inputStack.Collectible.GetMeltingDuration(world, inventory, firepit.inputSlot);
        ItemStack? queuedFuel = firepit.fuelStack;

        // Queued fuel remains separate from external sources; source restrictions govern candidate availability.
        List<ItemSlot> suitableExternalSlots = externalSlots
            .Where(AlloyTransferInventoryPolicy.CanWithdraw)
            .Where(slot => IsSuitableFuel(world, slot.Itemstack!, meltingPoint, firepit.HeatModifier))
            .ToList();
        List<ItemStack> candidates = suitableExternalSlots
            .Select(static slot => slot.Itemstack!)
            .ToList();
        if (queuedFuel is not null && IsSuitableFuel(world, queuedFuel, meltingPoint, firepit.HeatModifier))
        {
            candidates.Insert(0, queuedFuel);
        }

        ItemStack? bestStack = null;
        int bestAmount = 0;
        foreach (ItemStack stack in candidates)
        {
            if (!IsSuitableFuel(world, stack, meltingPoint, firepit.HeatModifier)) continue;
            if (bestStack is not null && SameStack(world, bestStack, stack)) continue;

            CombustibleProperties properties = stack.Collectible.GetCombustibleProperties(world, stack, null)!;
            int required = AlloyCalculatorLogic.CalculateFuelRequired(
                inputStack.StackSize,
                meltingPoint,
                meltingDuration,
                firepit.furnaceTemperature,
                firepit.InputStackTemp,
                firepit.inputStackCookingTime,
                firepit.fuelBurnTime,
                firepit.maxTemperature,
                properties.BurnDuration * firepit.BurnDurationModifier,
                (int)(properties.BurnTemperature * firepit.HeatModifier));

            int available = suitableExternalSlots
                .Where(slot => SameStack(world, slot.Itemstack!, stack))
                .Sum(static slot => slot.StackSize);
            if (queuedFuel is not null && SameStack(world, queuedFuel, stack))
            {
                available += queuedFuel.StackSize;
            }
            if (required > available || required > stack.Collectible.MaxStackSize) continue;

            if (bestStack is null || required < bestAmount)
            {
                bestStack = stack;
                bestAmount = required;
            }
        }

        if (bestStack is null)
        {
            return AlloyDepositResultCode.InsufficientItems;
        }

        plan = new AlloyFuelDepositPlan(
            bestStack.Clone(),
            bestAmount,
            queuedFuel?.Clone(),
            firepit.fuelSlot.StackSize);
        return AlloyDepositResultCode.Success;
    }

    /// <summary>Executes fuel correction using the current player inventory manager.</summary>
    internal static AlloyDepositResultCode ExecutePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        AlloyFuelDepositPlan plan)
    {
        return ExecutePlan(api, firepit, plan, api.World.Player.InventoryManager);
    }

    /// <summary>Executes a fuel plan through the supplied inventory manager using vanilla client packets.</summary>
    internal static AlloyDepositResultCode ExecutePlan(
        ICoreClientAPI api,
        BlockEntityFirepit firepit,
        AlloyFuelDepositPlan plan,
        IPlayerInventoryManager playerInventory)
    {
        IWorldAccessor world = api.World;
        if (firepit.Inventory is not InventorySmelting inventory
            || !playerInventory.OpenedInventories.Contains(inventory))
        {
            return AlloyDepositResultCode.InventoryClosed;
        }

        ItemStack? currentStack = firepit.fuelStack;
        bool currentStackMatches = currentStack is null
            ? plan.ExpectedCurrentStack is null
            : plan.ExpectedCurrentStack is not null
                && SameStack(world, currentStack, plan.ExpectedCurrentStack);
        if (!currentStackMatches || firepit.fuelSlot.StackSize != plan.ExpectedCurrentAmount)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        if (!AlloyTransferInventoryPolicy.TryCollect(world, playerInventory, inventory, out IReadOnlyList<ItemSlot> externalSlots))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        // Fresh slots and live eligibility probes prevent stale containers from participating in subsequent moves.
        InventorySlotCorrectionResult removeResult = ClientInventorySlotReconciler.RemoveIncorrectOrExcess(
            api,
            playerInventory,
            firepit.fuelSlot,
            externalSlots,
            stack => SameStack(world, stack, plan.DesiredStack),
            plan.DesiredAmount,
            out int retainedAmount,
            () => playerInventory.OpenedInventories.Contains(inventory),
            slot => AlloyTransferInventoryPolicy.IsCurrentExternalSlot(world, playerInventory, inventory, slot));
        if (removeResult != InventorySlotCorrectionResult.Success)
        {
            return MapResult(removeResult);
        }

        InventorySlotCorrectionResult addResult = ClientInventorySlotReconciler.AddMissing(
            api,
            playerInventory,
            firepit.fuelSlot,
            externalSlots,
            stack => SameStack(world, stack, plan.DesiredStack),
            plan.DesiredAmount - retainedAmount,
            () => playerInventory.OpenedInventories.Contains(inventory),
            slot => AlloyTransferInventoryPolicy.IsCurrentExternalSlot(world, playerInventory, inventory, slot));
        return MapResult(addResult);
    }

    #endregion

    #region Private

    /// <summary>Requires burning fuel that reaches the metals' melting point under the firepit's heat modifier.</summary>
    private static bool IsSuitableFuel(IWorldAccessor world, ItemStack stack, float meltingPoint, float heatModifier)
    {
        CombustibleProperties? properties = stack.Collectible.GetCombustibleProperties(world, stack, null);
        return properties is { BurnDuration: > 0 }
            && properties.BurnTemperature > 0
            && (int)(properties.BurnTemperature * heatModifier) >= meltingPoint;
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

    /// <summary>Determines whether two stacks can represent the same desired fuel-slot state.</summary>
    private static bool SameStack(IWorldAccessor world, ItemStack left, ItemStack right)
    {
        return left.Equals(world, right, GlobalConstants.IgnoredStackAttributes);
    }

    #endregion
}
