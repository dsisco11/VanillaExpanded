using System;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

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

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockBehaviorClass(BlockBehaviorIgnitionTool.RegistryId, typeof(BlockBehaviorIgnitionTool));
        api.RegisterCollectibleBehaviorClass(CollectibleBehaviorIgnitionTool.RegistryId, typeof(CollectibleBehaviorIgnitionTool));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        AddCanIgniteBehaviorToBlocks(api);
        AddCanIgniteBehaviorToItems(api);
    }
    #endregion

    #region CanIgnite Behavior
    private static bool IsIgnitionTool(ReadOnlySpan<char> code) => code.Length switch
    {
        6 => code.SequenceEqual("candle"),
        7 => code.SequenceEqual("lantern") || code.SequenceEqual("oillamp"),
        _ => false
    };

    /// <summary>
    /// Adds the CanIgnite behavior to blocks (lanterns, oil lamps) so they can ignite other blocks.
    /// </summary>
    private void AddCanIgniteBehaviorToBlocks(ICoreAPI api)
    {
        int addedCount = 0;
        var targetBlocks = api.World.Blocks.Where(static b => b is not null && IsIgnitionTool(b.Code?.FirstCodePart()));
        foreach (var block in targetBlocks)
        {
            // Skip if already has CanIgnite
            bool hasCanIgnite = block.BlockBehaviors?.Any(static b => b is BlockBehaviorIgnitionTool) == true;
            if (hasCanIgnite)
                continue;

            // Add the behavior to both arrays
            var behavior = new BlockBehaviorIgnitionTool(block);
            behavior.OnLoaded(api);
            block.BlockBehaviors = [behavior, .. block.BlockBehaviors ?? []];
            block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors ?? []];
            // remove the default CanIgnite behavior if it exists
            block.BlockBehaviors = block.BlockBehaviors?.Where(static b => b is not BlockBehaviorCanIgnite).ToArray();
            block.CollectibleBehaviors = block.CollectibleBehaviors?.Where(static b => b is not BlockBehaviorCanIgnite).ToArray();
            addedCount++;
        }

        if (addedCount > 0)
        {
            api.Logger.Notification($"[VanillaExpanded] Added CanIgnite behavior to {addedCount} blocks");
        }
    }

    /// <summary>
    /// Adds the CollectibleBehaviorCanIgnite to items (candles) so they can ignite other blocks.
    /// </summary>
    private void AddCanIgniteBehaviorToItems(ICoreAPI api)
    {
        int addedCount = 0;
        var targetItems = api.World.Items.Where(static i => i is not null && IsIgnitionTool(i.Code?.FirstCodePart()));
        foreach (var item in targetItems)
        {
            // Skip if already has CanIgnite
            bool hasCanIgnite = item.CollectibleBehaviors?.Any(static b => b is CollectibleBehaviorIgnitionTool) == true;
            if (hasCanIgnite)
                continue;

            // Add the behavior
            var behavior = new CollectibleBehaviorIgnitionTool(item);
            behavior.OnLoaded(api);
            item.CollectibleBehaviors = [behavior, .. item.CollectibleBehaviors ?? []];
            addedCount++;
        }

        if (addedCount > 0)
        {
            api.Logger.Notification($"[VanillaExpanded] Added CanIgnite behavior to {addedCount}");
        }
    }
    #endregion
}
