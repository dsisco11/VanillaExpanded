using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.AutoStashing;

/// <summary>Preserves the owning target's established audit formats independently of transfer coordination.</summary>
internal sealed class AutoStashAuditContext(BlockPos position, string name, bool bloomery = false)
{
    #region Public API
    /// <summary>Records actual movement using the original target-specific format.</summary>
    public void Moved(IWorldAccessor world, string player, int quantity, AssetLocation? code)
    {
        if (bloomery)
            world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into bloomery at <{3}>.", player, quantity, code, position);
        else
            world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3} at <{4}>.", player, quantity, code, name, position);
    }

    /// <summary>Records the completed actual total after successful finalization and cleanup.</summary>
    public void Completed(IWorldAccessor world, string player, int quantity)
    {
        if (bloomery)
            world.Api?.World.Logger.Audit("'{0}' auto-stashed {1} items into bloomery at <{2}>.", player, quantity, position);
        else
            world.Api?.World.Logger.Audit("'{0}' auto-stashed {1} items into {2} at <{3}>.", player, quantity, name, position);
    }
    #endregion
}
