using System.Runtime.CompilerServices;

using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing;

/// <summary>
/// Provides access to the internal bloomeryInv field of <see cref="BlockEntityBloomery"/>
/// using Harmony's FieldRefAccess pattern.
/// </summary>
internal static class BloomeryAccessor
{
    /// <summary>
    /// Delegate for accessing the internal bloomeryInv field by reference.
    /// </summary>
    private static readonly AccessTools.FieldRef<BlockEntityBloomery, InventoryGeneric> BloomeryInvRef =
        AccessTools.FieldRefAccess<BlockEntityBloomery, InventoryGeneric>("bloomeryInv");

    /// <summary>
    /// Gets the inventory from a <see cref="BlockEntityBloomery"/> instance.
    /// </summary>
    /// <param name="bloomery">The bloomery block entity.</param>
    /// <returns>The bloomery's internal inventory, or null if the bloomery is null.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static InventoryGeneric? GetInventory(BlockEntityBloomery? bloomery)
    {
        return bloomery is null ? null : BloomeryInvRef(bloomery);
    }
}
