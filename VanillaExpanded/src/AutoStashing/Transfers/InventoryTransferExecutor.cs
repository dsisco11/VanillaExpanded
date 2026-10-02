using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Transfers;

/// <summary>Delegates one concrete move to engine slots without owning selection, sessions or persistence.</summary>
internal static class InventoryTransferExecutor
{
    #region Public API
    /// <summary>Executes the supplied settings and returns actual movement; engine and callback exceptions propagate unchanged.</summary>
    public static InventoryTransferResult Execute(IWorldAccessor world, InventoryTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.Invocation == InventoryTransferInvocation.EngineDefaults)
        {
            // Matching default settings alone would bypass overrides of the convenience overload.
            // That overload exposes only actual movement, so no required priority can be observed here.
            int defaultMoved = transfer.Source.TryPutInto(world, transfer.Destination, transfer.RequestedQuantity);
            return new InventoryTransferResult(transfer.RequestedQuantity, defaultMoved, null);
        }
        // Keep the operation local: the engine remains authoritative for restrictions and modification callbacks.
        ItemStackMoveOperation operation = new(world, transfer.MouseButton, transfer.Modifiers,
            transfer.Priority, transfer.RequestedQuantity);
        int moved = transfer.Source.TryPutInto(transfer.Destination, ref operation);
        return new InventoryTransferResult(transfer.RequestedQuantity, moved, operation.RequiredPriority);
    }
    #endregion
}
