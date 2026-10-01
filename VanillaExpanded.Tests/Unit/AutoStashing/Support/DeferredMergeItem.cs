using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Models collectible merge outcomes at the extension boundary while retaining engine mutation on successful merges.</summary>
internal sealed class DeferredMergeItem : MockItem
{
    public bool RejectDirect { get; set; }
    public bool RepeatRequiredPriority { get; set; }

    #region Public API
    /// <summary>Creates a merge-compatible collectible with a stable identity.</summary>
    public DeferredMergeItem(ICoreAPI api) : base(7, api: api)
    {
        Code = new AssetLocation("game:deferred-merge");
        MaxStackSize = 64;
    }

    /// <summary>Requests direct priority for automatic attempts and applies the configured direct rejection.</summary>
    public override void TryMergeStacks(ItemStackMergeOperation op)
    {
        if (op.CurrentPriority == EnumMergePriority.AutoMerge || RejectDirect)
        {
            op.MovedQuantity = 0;
            if (op.CurrentPriority == EnumMergePriority.AutoMerge || RepeatRequiredPriority)
                op.RequiredPriority = EnumMergePriority.DirectMerge;
            return;
        }
        base.TryMergeStacks(op);
    }
    #endregion
}
