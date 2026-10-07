using System;
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
    internal static AlloyDepositResultCode Execute(
        ICoreClientAPI api,
        BlockEntityFirepit firepit)
    {
        AlloyDepositResultCode result = CreatePlan(api, firepit, out AlloyFuelDepositPlan? plan);
        return result == AlloyDepositResultCode.Success
            ? ExecutePlan(api, firepit, plan!)
            : result;
    }

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

        IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (backpack is null || hotbar is null)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        ItemStack inputStack = firepit.inputStack;
        float meltingPoint = inputStack.Collectible.GetMeltingPoint(world, inventory, firepit.inputSlot);
        float meltingDuration = inputStack.Collectible.GetMeltingDuration(world, inventory, firepit.inputSlot);
        List<ItemSlot> playerSlots = [.. backpack, .. hotbar];
        ItemStack? queuedFuel = firepit.fuelStack;

        List<ItemSlot> suitablePlayerSlots = playerSlots
            .Where(static slot => slot.Itemstack is not null)
            .Where(slot => IsSuitableFuel(world, slot.Itemstack!, meltingPoint, firepit.HeatModifier))
            .ToList();
        List<ItemStack> candidates = suitablePlayerSlots
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

            int available = suitablePlayerSlots
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

        IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (backpack is null || hotbar is null)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        List<ItemSlot> playerSlots = [.. backpack, .. hotbar];
        InventorySlotCorrectionResult removeResult = ClientInventorySlotReconciler.RemoveIncorrectOrExcess(
            api,
            playerInventory,
            firepit.fuelSlot,
            playerSlots,
            stack => SameStack(world, stack, plan.DesiredStack),
            plan.DesiredAmount,
            out int retainedAmount);
        if (removeResult != InventorySlotCorrectionResult.Success)
        {
            return MapResult(removeResult);
        }

        InventorySlotCorrectionResult addResult = ClientInventorySlotReconciler.AddMissing(
            api,
            playerInventory,
            firepit.fuelSlot,
            playerSlots,
            stack => SameStack(world, stack, plan.DesiredStack),
            plan.DesiredAmount - retainedAmount);
        return MapResult(addResult);
    }

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

}
