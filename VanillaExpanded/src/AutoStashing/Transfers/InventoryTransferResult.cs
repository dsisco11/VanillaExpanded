using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Transfers;

/// <summary>Reports the original request, actual engine movement and the engine's required merge priority.</summary>
internal readonly struct InventoryTransferResult
{
    public int RequestedQuantity { get; }
    public int MovedQuantity { get; }
    /// <summary>Engine-required priority, or null when none was requested or the convenience overload exposes no operation.</summary>
    public EnumMergePriority? RequiredPriority { get; }

    #region Public API
    /// <summary>Records an engine outcome without treating the requested quantity as successful movement.</summary>
    public InventoryTransferResult(int requestedQuantity, int movedQuantity, EnumMergePriority? requiredPriority)
    {
        RequestedQuantity = requestedQuantity;
        MovedQuantity = movedQuantity;
        RequiredPriority = requiredPriority;
    }
    #endregion
}
