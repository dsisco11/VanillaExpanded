using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Controls only priority eligibility while leaving storage checks and mutation with engine slots.</summary>
internal sealed class PriorityRestrictedSlot : ItemSlot
{
    public bool AllowAutomatic { get; set; }

    #region Public API
    /// <summary>Creates a destination that initially permits selection only at direct priority.</summary>
    public PriorityRestrictedSlot(InventoryBase inventory) : base(inventory) { }

    /// <summary>Applies the priority gate before the engine's normal capacity and storage checks.</summary>
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        return (AllowAutomatic || priority == EnumMergePriority.DirectMerge) && base.CanTakeFrom(sourceSlot, priority);
    }
    #endregion
}
