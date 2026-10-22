using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Admits bounded upload work per frame while resident receipts bypass every exhausted limit.</summary>
internal sealed class HabitatUploadBudget
{
    private readonly HabitatUploadLimits limits;
    private readonly TimeProvider time;
    private long started;
    internal HabitatUploadCost Used { get; private set; }

    #region Public API
    /// <summary>Creates a fresh frame budget; no clock read occurs until actual upload work is admitted.</summary>
    internal HabitatUploadBudget(HabitatUploadLimits limits, TimeProvider? time = null)
    { this.limits = limits; this.time = time ?? TimeProvider.System; }
    /// <summary>Reserves one coherent receipt's missing resources, allowing one initial oversized admission for progress.</summary>
    internal bool TryAdmit(HabitatUploadCost cost)
    {
        if (cost.TransferBytes < 0 || cost.AllocationBytes < 0 || cost.Transfers < 0 || cost.Allocations < 0)
            throw new ArgumentOutOfRangeException(nameof(cost));
        if (cost.TotalBytes == 0 && cost.Operations == 0) return true;
        var next = Used + cost;
        // A single native allocation/transfer cannot be preempted; the next admission observes elapsed time.
        if (Used.Operations != 0 && (next.TotalBytes > limits.Bytes || next.Operations > limits.Operations
            || time.GetElapsedTime(started) >= limits.Elapsed)) return false;
        if (Used.Operations == 0) started = time.GetTimestamp();
        Used = next;
        return true;
    }
    #endregion
}
