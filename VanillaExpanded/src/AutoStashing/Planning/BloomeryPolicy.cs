using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Owns bloomery input classification, mandatory routing and live ore/fuel allowances.</summary>
internal sealed class BloomeryPolicy : AutoStashPolicy
{
    private readonly BlockEntityBloomery bloomery;
    private readonly InventoryGeneric inventory;

    #region Public API
    #region Operation rules
    /// <summary>Uses the actual bloomery and its live inventory rather than a simulated capacity model.</summary>
    public BloomeryPolicy(BlockEntityBloomery bloomery, InventoryGeneric inventory)
    {
        this.bloomery = bloomery;
        this.inventory = inventory;
    }

    /// <summary>Orders backpack ore/fuel before hotbar ore/fuel, preserving per-inventory traversal.</summary>
    public override IEnumerable<AutoStashSourcePass> GetSourcePasses(IInventory? backpack, IInventory? hotbar)
    {
        foreach (IInventory? source in new[] { backpack, hotbar })
        {
            if (source is null) continue;
            yield return new AutoStashSourcePass(source, 1);
            yield return new AutoStashSourcePass(source, 0);
        }
    }

    /// <summary>Checks engine item acceptance before classification, without introducing the client active-item gate.</summary>
    public override bool IsEligible(ItemStack stack, AutoStashSourcePass pass)
        => bloomery.CanAdd(stack) && Classify(stack) == pass.RequiredSlot;

    /// <summary>Recognizes valid input types even when live input capacity is exhausted.</summary>
    public override bool IsCandidate(ItemStack stack, AutoStashSourcePass pass) => Classify(stack) == pass.RequiredSlot;

    /// <summary>Limits the request to remaining capacity using contents left by preceding actual moves.</summary>
    public override int GetQuantity(ItemSlot source, AutoStashSourcePass pass)
        => Math.Min(source.StackSize, GetMaxCanAdd(inventory, source.Itemstack!, pass.RequiredSlot!.Value));
    #endregion

    #region Classification and capacity
    /// <summary>Rejects a burning bloomery or an occupied output before an operation starts.</summary>
    public static bool IsAvailable(BlockEntityBloomery bloomery, InventoryGeneric inventory)
        => !bloomery.IsBurning && inventory[2].Empty;

    /// <summary>Computes remaining fuel from actual ore, or ore from its configured ratio; output is never a destination.</summary>
    public static int GetMaxCanAdd(InventoryGeneric inventory, ItemStack stack, int slotIndex)
    {
        const int FuelCapacity = 6;
        if (slotIndex == 0)
        {
            int oreSize = inventory[1].StackSize;
            int ratio = GetOre2FuelRatio(inventory[1].Itemstack);
            int required = oreSize > 0 ? (int)Math.Ceiling((float)oreSize / ratio) : FuelCapacity;
            return Math.Max(0, required - inventory[0].StackSize);
        }
        if (slotIndex == 1) return Math.Max(0, GetOre2FuelRatio(stack) * FuelCapacity - inventory[1].StackSize);
        return 0;
    }

    /// <summary>Classifies ore before fuel with the established temperature and duration boundaries.</summary>
    public static int? Classify(ItemStack stack)
    {
        if (stack?.Collectible?.CombustibleProps is not CombustibleProperties props) return null;
        if (props.SmeltedStack is not null && props.MeltingPoint >= BlockEntityBloomery.MinTemp
            && props.MeltingPoint < BlockEntityBloomery.MaxTemp) return 1;
        if (props.BurnTemperature >= 1200 && props.BurnDuration > 30) return 0;
        return null;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Uses the configured fuel ratio and retains the nonpositive fallback without changing engine CanAdd.</summary>
    private static int GetOre2FuelRatio(ItemStack? stack)
    {
        if (stack?.Collectible?.CombustibleProps is not CombustibleProperties props) return 1;
        int ratio = stack.ItemAttributes?["bloomeryFuelRatio"].AsInt(props.SmeltedRatio) ?? props.SmeltedRatio;
        return Math.Max(1, ratio);
    }
    #endregion
}
