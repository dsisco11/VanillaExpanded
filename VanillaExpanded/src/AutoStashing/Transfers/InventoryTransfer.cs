using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Transfers;

/// <summary>Describes one immediate engine move; eligibility and live limits remain the caller's responsibility.</summary>
internal sealed class InventoryTransfer
{
    public ItemSlot Source { get; }
    public ItemSlot Destination { get; }
    public int RequestedQuantity { get; }
    public EnumMouseButton MouseButton { get; }
    public EnumModifierKey Modifiers { get; }
    public EnumMergePriority Priority { get; }
    public InventoryTransferInvocation Invocation { get; }

    #region Public API
    /// <summary>Preserves the engine-default convenience overload, including overrides of that virtual boundary.</summary>
    public InventoryTransfer(ItemSlot source, ItemSlot destination, int requestedQuantity)
        : this(source, destination, requestedQuantity, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge)
    {
        Invocation = InventoryTransferInvocation.EngineDefaults;
    }

    /// <summary>Captures concrete slots and engine settings, rejecting structurally invalid instructions without evaluating policy.</summary>
    public InventoryTransfer(ItemSlot source, ItemSlot destination, int requestedQuantity,
        EnumMouseButton mouseButton, EnumModifierKey modifiers, EnumMergePriority priority)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (requestedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(requestedQuantity));
        Source = source;
        Destination = destination;
        RequestedQuantity = requestedQuantity;
        MouseButton = mouseButton;
        Modifiers = modifiers;
        Priority = priority;
        Invocation = InventoryTransferInvocation.ExplicitOperation;
    }
    #endregion
}
