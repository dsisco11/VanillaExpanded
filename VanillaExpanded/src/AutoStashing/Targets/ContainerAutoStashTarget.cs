using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing.Targets;

/// <summary>Owns ordinary container and crate session lifecycle and block-entity synchronization.</summary>
internal sealed class ContainerAutoStashTarget : InventoryAutoStashTarget
{
    private readonly BlockEntityContainer container;

    #region Public API
    /// <summary>Resolves the owning container's inventory without replacing specialized crate restrictions.</summary>
    public ContainerAutoStashTarget(BlockEntityContainer container) : base(container.Inventory) => this.container = container;

    /// <summary>Uses the container's owning contents API for initial matching-type capture.</summary>
    public override IEnumerable<ItemStack?> GetContents() => container.GetNonEmptyContentStacks();

    /// <summary>Marks the owning block entity dirty after applied or uncertain engine movement.</summary>
    public override void FinalizeChanges() => container.MarkDirty();
    #endregion
}
