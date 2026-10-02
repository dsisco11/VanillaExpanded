namespace VanillaExpanded.AutoStashing.Transfers;

/// <summary>Identifies the original engine overload whose virtual dispatch must be preserved.</summary>
internal enum InventoryTransferInvocation
{
    /// <summary>Pass a move operation by reference and observe its required priority.</summary>
    ExplicitOperation,
    /// <summary>Use the world/quantity convenience overload with engine defaults and no exposed operation result.</summary>
    EngineDefaults
}
