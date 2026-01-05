using System.Linq;

using Vintagestory.API.Common;

namespace VanillaExpanded.IgnitionTools;

/// <summary>
/// Mod system that fixes ignition tool priority for blocks with the CanIgnite behavior.
/// This ensures lanterns, torches, and other ignition sources can properly ignite blocks
/// like bloomeries that would otherwise consume the interaction.
/// </summary>
public sealed class IgnitionToolsModSystem : ModSystem
{
    #region ModSystem Lifecycle
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return VanillaExpandedModSystem.Config.EnableIgnitionTools;
    }

    public override double ExecuteOrder()
    {
        // Run after blocks are loaded
        return 1.0;
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        FixIgnitionToolPriority(api);
    }
    #endregion

    #region Priority Fix
    /// <summary>
    /// Sets HeldPriorityInteract=true on blocks with CanIgnite behavior.
    /// This ensures the held item's OnHeldInteractStart runs before the block's OnBlockInteractStart
    /// when sneaking, allowing the ignition behavior to prevent block interactions like the bloomery's
    /// item insertion from consuming the click.
    /// </summary>
    private void FixIgnitionToolPriority(ICoreAPI api)
    {
        int fixedCount = 0;
        foreach (var block in api.World.Blocks)
        {
            if (block is null) continue;

            bool hasCanIgnite = block.CollectibleBehaviors?.Any(b => b.GetType().Name == "BlockBehaviorCanIgnite") == true;
            if (hasCanIgnite && !block.HeldPriorityInteract)
            {
                block.HeldPriorityInteract = true;
                fixedCount++;
            }
        }

        if (fixedCount > 0)
        {
            api.Logger.Notification($"[VanillaExpanded] Set HeldPriorityInteract=true on {fixedCount} blocks with CanIgnite behavior");
        }
    }
    #endregion
}
