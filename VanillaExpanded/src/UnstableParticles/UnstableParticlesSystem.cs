using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.UnstableParticles;

/// <summary>
/// Client-side mod system that adds crumbling dust particles to unstable rock blocks.
/// Fully self-contained - handles behavior registration and application.
/// </summary>
internal sealed class UnstableParticlesSystem : ModSystem
{
    #region ModSystem Lifecycle
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client && VanillaExpandedModSystem.Config.EnableUnstableParticles;
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockBehaviorClass(BlockBehaviorUnstableParticles.RegistryId, typeof(BlockBehaviorUnstableParticles));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);

        // Only apply behaviors if cave-ins are enabled in world config
        if (!IsCaveInsEnabled(api))
        {
            api.Logger.Debug("[UnstableParticlesSystem] Cave-ins are disabled, skipping unstable particle behaviors.");
            return;
        }

        AmendUnstableRockBehaviors(api);
    }

    public override void Dispose()
    {
        BlockBehaviorUnstableParticles.ClearCache();
        base.Dispose();
    }
    #endregion

    #region Behavior Application
    /// <summary>
    /// Find all block types which have BlockBehaviorUnstableRock and add our particle behavior to them.
    /// </summary>
    private static void AmendUnstableRockBehaviors(ICoreAPI api)
    {
        int amendedCount = 0;

        foreach (Block block in api.World.Blocks)
        {
            if (block?.Code is null)
            {
                continue;
            }

            // Only add to blocks that have the UnstableRock behavior (and don't already have our behavior)
            if (block.HasBehavior<BlockBehaviorUnstableRock>() && !block.HasBehavior<BlockBehaviorUnstableParticles>())
            {
                BlockBehaviorUnstableParticles behavior = new(block);

                // Add behavior to block-behaviors array (prepend)
                block.BlockBehaviors = [behavior, .. block.BlockBehaviors];

                // Add behavior to collectible-behaviors array (prepend)
                block.CollectibleBehaviors = [behavior, .. block.CollectibleBehaviors];

                amendedCount++;
            }
        }

        api.Logger.Debug($"[UnstableParticlesSystem] Added unstable particle behavior to {amendedCount} blocks.");
    }
    #endregion

    #region World Config
    /// <summary>
    /// Checks if cave-ins are enabled in the world configuration.
    /// </summary>
    private static bool IsCaveInsEnabled(ICoreAPI api)
    {
        string? caveInsSetting = api.World.Config.GetAsString("caveIns", "on");
        return caveInsSetting == "on";
    }
    #endregion
}
