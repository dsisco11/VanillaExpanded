using System;
using System.Collections.Generic;
using System.Linq;

using VanillaExpanded.Network;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.AlloyCalculator;

internal static class AlloyDepositService
{
    private const int MaxIngredientAmount = 100_000;

    internal static AlloyDepositResultCode Execute(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        BlockEntityFirepit firepit,
        Packet_RequestAlloyDeposit request)
    {
        return Execute(world, playerInventory, firepit, request, world.Api.GetMetalAlloys());
    }

    internal static AlloyDepositResultCode Execute(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        BlockEntityFirepit firepit,
        Packet_RequestAlloyDeposit request,
        IReadOnlyList<AlloyRecipe> recipes)
    {
        if (firepit.Inventory is not InventorySmelting inventory
            || request.SlotIndices is null
            || request.SlotIngredientCodes is null
            || request.SlotAmounts is null
            || request.SlotIndices.Length == 0
            || request.SlotIndices.Length != request.SlotIngredientCodes.Length
            || request.SlotIndices.Length != request.SlotAmounts.Length
            || request.SlotAmounts.Any(static amount => amount <= 0 || amount > MaxIngredientAmount))
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        if (!playerInventory.OpenedInventories.Contains(inventory))
        {
            return AlloyDepositResultCode.InventoryClosed;
        }

        AlloyRecipe? recipe = recipes.FirstOrDefault(recipe =>
            recipe.Enabled
            && recipe.Output?.Code?.ToString() == request.AlloyCode);

        IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (backpack is null || hotbar is null)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        ItemSlot[] cookingSlots = inventory.CookingSlots;
        if (cookingSlots.Length == 0)
        {
            return AlloyDepositResultCode.InvalidRequest;
        }

        if (recipe is null || !TryBuildPlan(recipe, request, cookingSlots.Length, out List<SlotTarget> targets))
        {
            return AlloyDepositResultCode.InvalidRecipe;
        }

        List<ItemSlot> playerSlots = [.. backpack, .. hotbar];
        List<ItemSlot> allSlots = [.. playerSlots, .. cookingSlots];
        List<SlotSnapshot> snapshot = allSlots
            .Distinct()
            .Select(static slot => new SlotSnapshot(slot, slot.Itemstack?.Clone()))
            .ToList();

        try
        {
            foreach (ItemSlot cookingSlot in cookingSlots)
            {
                if (!MoveEntireStack(world, playerInventory, cookingSlot, playerSlots))
                {
                    Restore(snapshot);
                    return AlloyDepositResultCode.InsufficientSpace;
                }
            }

            foreach (SlotTarget target in targets)
            {
                if (!MoveIngredient(
                    world,
                    playerInventory,
                    playerSlots,
                    cookingSlots[target.SlotIndex],
                    target.Ingredient,
                    target.Amount))
                {
                    Restore(snapshot);
                    return AlloyDepositResultCode.InsufficientItems;
                }
            }

            if (!recipe.Matches(cookingSlots.Select(static slot => slot.Itemstack).ToArray()))
            {
                Restore(snapshot);
                return AlloyDepositResultCode.InvalidRecipe;
            }

            foreach (ItemSlot slot in allSlots)
            {
                slot.MarkDirty();
            }

            firepit.MarkDirty(true);
            return AlloyDepositResultCode.Success;
        }
        catch
        {
            Restore(snapshot);
            return AlloyDepositResultCode.TransferFailed;
        }
    }

    private static bool TryBuildPlan(
        AlloyRecipe recipe,
        Packet_RequestAlloyDeposit request,
        int cookingSlotCount,
        out List<SlotTarget> targets)
    {
        targets = [];
        var ingredientsByCode = recipe.Ingredients
            .Where(static ingredient => ingredient.Code is not null && ingredient.ResolvedItemstack is not null)
            .ToDictionary(static ingredient => ingredient.Code.ToString(), StringComparer.Ordinal);
        if (ingredientsByCode.Count != recipe.Ingredients.Length)
        {
            return false;
        }

        var usedSlots = new HashSet<int>();
        var requestedAmounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < request.SlotIndices.Length; index++)
        {
            int slotIndex = request.SlotIndices[index];
            string code = request.SlotIngredientCodes[index];
            if (slotIndex < 0
                || slotIndex >= cookingSlotCount
                || !usedSlots.Add(slotIndex)
                || string.IsNullOrWhiteSpace(code)
                || !ingredientsByCode.TryGetValue(code, out MetalAlloyIngredient? ingredient))
            {
                return false;
            }

            requestedAmounts[code] = requestedAmounts.GetValueOrDefault(code) + request.SlotAmounts[index];
            targets.Add(new SlotTarget(slotIndex, ingredient, request.SlotAmounts[index]));
        }

        foreach (MetalAlloyIngredient ingredient in recipe.Ingredients)
        {
            string code = ingredient.Code?.ToString() ?? string.Empty;
            if (code.Length == 0 || !requestedAmounts.TryGetValue(code, out int amount) || ingredient.ResolvedItemstack is null)
            {
                return false;
            }
        }

        if (requestedAmounts.Count != recipe.Ingredients.Length)
        {
            return false;
        }

        ItemStack[] requestedStacks = recipe.Ingredients
            .Select(ingredient =>
            {
                ItemStack stack = ingredient.ResolvedItemstack!.Clone();
                stack.StackSize = requestedAmounts[ingredient.Code.ToString()];
                return stack;
            })
            .ToArray();

        return recipe.Matches(requestedStacks, false);
    }

    private static bool MoveEntireStack(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        ItemSlot source,
        IReadOnlyList<ItemSlot> targets)
    {
        while (!source.Empty)
        {
            int before = source.StackSize;
            foreach (ItemSlot target in targets)
            {
                if (!target.CanTakeFrom(source)) continue;

                Move(world, playerInventory, source, target, before);
                if (source.Empty) return true;
                if (source.StackSize < before) break;
            }

            if (source.StackSize == before)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MoveIngredient(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        IReadOnlyList<ItemSlot> sourceSlots,
        ItemSlot targetSlot,
        MetalAlloyIngredient ingredient,
        int amount)
    {
        int remaining = amount;
        foreach (ItemSlot sourceSlot in sourceSlots)
        {
            if (remaining <= 0) break;
            if (sourceSlot.Empty || ingredient.ResolvedItemstack is null
                || !SmeltsInto(world, sourceSlot.Itemstack, ingredient.ResolvedItemstack)) continue;

            remaining -= Move(world, playerInventory, sourceSlot, targetSlot, remaining);
        }

        return remaining == 0;
    }

    private static bool SmeltsInto(IWorldAccessor world, ItemStack source, ItemStack target)
    {
        ItemStack? smelted = source.Collectible
            .GetCombustibleProperties(world, source, null)?
            .SmeltedStack?
            .ResolvedItemstack;

        return target.Equals(world, smelted, GlobalConstants.IgnoredStackAttributes);
    }

    private static int Move(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        ItemSlot source,
        ItemSlot target,
        int quantity)
    {
        var operation = new ItemStackMoveOperation(
            world,
            EnumMouseButton.Left,
            EnumModifierKey.SHIFT,
            EnumMergePriority.AutoMerge,
            quantity);

        _ = playerInventory.TryTransferTo(source, target, ref operation);
        return operation.MovedQuantity;
    }

    private static void Restore(IEnumerable<SlotSnapshot> snapshot)
    {
        foreach (SlotSnapshot item in snapshot)
        {
            item.Slot.Itemstack = item.Stack?.Clone();
            item.Slot.MarkDirty();
        }
    }

    private sealed record SlotTarget(int SlotIndex, MetalAlloyIngredient Ingredient, int Amount);
    private sealed record SlotSnapshot(ItemSlot Slot, ItemStack? Stack);
}
