using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Finds carried chisel materials without transferring stacks or changing target quantities.</summary>
internal static class ChiselMaterialCandidates
{
    #region Public API
    /// <summary>Snapshots eligible stacks in stable hotbar then backpack slot order.</summary>
    internal static IReadOnlyList<ChiselMaterialCandidate> Enumerate(ICoreAPI api, IPlayer player,
        BlockPos position, BlockEntityChisel target)
    {
        var candidates = new List<ChiselMaterialCandidate>();
        IPlayerInventoryManager manager = player.InventoryManager;
        // Only owned storage is searched; opened containers and equipment inventories never enter this list.
        foreach (string inventoryClass in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (manager.GetOwnInventory(inventoryClass) is not InventoryBase inventory) continue;
            bool backpack = inventoryClass == GlobalConstants.backpackInvClassName;
            for (int index = 0; index < inventory.Count; index++)
            {
                ItemSlot slot = inventory[index];
                bool storage = backpack ? slot is ItemSlotBagContent
                    : slot.GetType() == typeof(ItemSlotSurvival)
                        || slot is ItemSlotOffhand && ReferenceEquals(slot, manager.OffhandHotbarSlot);
                if (!storage || !ReferenceEquals(slot.Inventory, inventory)
                    || slot.Itemstack is not ItemStack stack || !IsEligible(api, player, position, target, stack)) continue;
                candidates.Add(new ChiselMaterialCandidate(inventory, index, stack, stack.Clone()));
            }
        }
        return candidates;
    }

    /// <summary>Checks material rules and useful remaining supply without invoking AddMaterial.</summary>
    internal static bool IsEligible(ICoreAPI api, IPlayer player, BlockPos position,
        BlockEntityChisel target, ItemStack stack)
    {
        if (stack.StackSize <= 0 || stack.Block is not Block block
            || target.BlockIds is null || target.AvailMaterialQuantities is null
            || target.BlockIds.Length != target.AvailMaterialQuantities.Length
            || !IsValidMaterial(api, player, position, block)) return false;
        if (block is not BlockChisel)
            return HasCapacity(target, block.Id) && VoxelQuantity(block, api.World.BlockAccessor, position) > 0;

        // Vanilla reads parallel arrays and casts each contribution to ushort. Reject unsafe data rather than losing a stack.
        int[]? materials = (stack.Attributes?["materials"] as IntArrayAttribute)?.value;
        int[]? quantities = (stack.Attributes?["availMaterialQuantities"] as IntArrayAttribute)?.value;
        if (materials is null || quantities is null || materials.Length != quantities.Length) return false;
        bool contributes = false;
        for (int index = 0; index < materials.Length; index++)
        {
            if (quantities[index] < 0 || quantities[index] > ushort.MaxValue) return false;
            if (materials[index] == 0 || quantities[index] == 0) continue;
            if (materials[index] < 0
                || api.World.Blocks is { } blocks && materials[index] >= blocks.Count) return false;
            Block? material = api.World.GetBlock(materials[index]);
            if (material is null || material.Id == 0) return false;
            contributes |= HasCapacity(target, material.Id);
        }
        return contributes;
    }

    /// <summary>Mirrors ItemChisel.IsValidChiselingMaterial without its client rejection notification.</summary>
    internal static bool IsValidMaterial(ICoreAPI api, IPlayer player, BlockPos position, Block block)
    {
        // Preserve vanilla priority and its two conditional calls. CanChisel receives the real world/player/target;
        // only TriggerIngameError is omitted so rejected inventory entries do not spam the player.
        if (block is BlockChisel) return true;
        string mode = api.World.Config.GetString("microblockChiseling");
        if (mode == "off") return false;
        IConditionalChiselable? conditional = block.GetInterface<IConditionalChiselable>(api.World, position);
        if (conditional is not null && (!conditional.CanChisel(api.World, position, player, out _)
            || !conditional.CanChisel(api.World, position, player, out _))) return false;
        bool flagSet = block.Attributes?["canChisel"].Exists == true;
        bool flag = block.Attributes?["canChisel"].AsBool(false) == true;
        if (flag) return true;
        if (flagSet) return false;
        if (block.DrawType != EnumDrawType.Cube && block.Shape?.Base.Path != "block/basic/cube") return false;
        if (block.HasBehavior<BlockBehaviorDecor>()) return false;
        if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return true;
        if (block.BlockMaterial is EnumBlockMaterial.Water or EnumBlockMaterial.Lava) return false;
        return mode != "stonewood" || block.Code.Path.Contains("mudbrick")
            || block.BlockMaterial is EnumBlockMaterial.Wood or EnumBlockMaterial.Stone or EnumBlockMaterial.Ore or EnumBlockMaterial.Ceramic;
    }
    #endregion

    #region Private
    /// <summary>Admits new materials and replenishment below vanilla's full-supply threshold.</summary>
    private static bool HasCapacity(BlockEntityChisel target, int blockId)
    {
        int index = Array.IndexOf(target.BlockIds, blockId);
        return index < 0 || target.AvailMaterialQuantities[index] < 16 * 16 * 16;
    }

    /// <summary>Computes the same ushort collision-box voxel contribution as native AddMaterial.</summary>
    private static ushort VoxelQuantity(Block block, IBlockAccessor accessor, BlockPos position)
    {
        Cuboidf[] boxes = block.GetCollisionBoxes(accessor, position) ?? [Cuboidf.Default()];
        int sum = 0;
        foreach (Cuboidf box in boxes)
            sum += new Cuboidi((int)(16 * box.X1), (int)(16 * box.Y1), (int)(16 * box.Z1),
                (int)(16 * box.X2), (int)(16 * box.Y2), (int)(16 * box.Z2)).SizeXYZ;
        return unchecked((ushort)sum);
    }
    #endregion
}
