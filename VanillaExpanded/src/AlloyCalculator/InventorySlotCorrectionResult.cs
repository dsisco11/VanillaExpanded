namespace VanillaExpanded.AlloyCalculator;

/// <summary>Describes the outcome of reconciling one inventory slot.</summary>
internal enum InventorySlotCorrectionResult
{
    Success,
    InsufficientSpace,
    InsufficientItems,
    TransferFailed
}