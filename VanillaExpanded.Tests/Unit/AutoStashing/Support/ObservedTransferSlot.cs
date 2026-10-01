using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Observes engine moves and optionally controls a bounded failure or quantity at the source-slot boundary.</summary>
internal sealed class ObservedTransferSlot : ItemSlot
{
    /// <summary>Creates a source slot owned by the supplied real engine inventory.</summary>
    public ObservedTransferSlot(InventoryBase inventory) : base(inventory) { }

    public List<MoveAttempt> Attempts { get; } = [];
    public int AttemptLimit { get; set; } = 16;
    public int? QuantityLimit { get; set; }
    public bool Reject { get; set; }
    public Action<ItemSlot>? BeforeMove { get; set; }
    public Action<ItemSlot>? AfterMove { get; set; }

    #region Public API
    /// <summary>Records a move, applies the configured boundary control, then delegates mutation to the engine.</summary>
    public override int TryPutInto(ItemSlot sinkSlot, ref ItemStackMoveOperation op)
    {
        if (Attempts.Count >= AttemptLimit)
        {
            throw new InvalidOperationException("Transfer observation exceeded its attempt limit.");
        }
        Attempts.Add(new MoveAttempt(sinkSlot, op.RequestedQuantity, op.CurrentPriority));
        BeforeMove?.Invoke(sinkSlot);
        if (Reject) return 0;
        if (QuantityLimit is int maximum) op.RequestedQuantity = Math.Min(op.RequestedQuantity, maximum);
        int moved = base.TryPutInto(sinkSlot, ref op);
        AfterMove?.Invoke(sinkSlot);
        return moved;
    }
    #endregion
}

/// <summary>Records the destination, requested quantity, and merge priority before an engine move.</summary>
internal sealed record MoveAttempt(ItemSlot Destination, int Quantity, EnumMergePriority Priority);
