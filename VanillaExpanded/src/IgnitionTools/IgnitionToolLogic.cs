using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VanillaExpanded.IgnitionTools;

/// <summary>
/// Contains the shared ignition logic used by both <see cref="CollectibleBehaviorIgnitionTool"/> 
/// and <see cref="BlockBehaviorIgnitionTool"/>.
/// </summary>
internal sealed class IgnitionToolLogic
{
    #region Constants
    /// <summary> Unique ID for the sound callback </summary>
    private const string SoundId = "ignition_tool_sound";
    #endregion

    #region Fields
    private readonly ICoreAPI? api;
    /// <summary> Sound to play when igniting </summary>
    private readonly string igniteSound = "sounds/torch-ignite";
    /// <summary> Animation to play when igniting </summary>
    private readonly string? igniteAnimation = null;
    /// <summary> Time in seconds it takes to use the tool </summary>
    private float IgnitionDelay => 5f; // VanillaExpandedModSystem.Config.IgnitionDelay;
    #endregion

    #region Initialization
    public IgnitionToolLogic(ICoreAPI? api)
    {
        this.api = api;
    }
    #endregion

    #region Item Interaction
    public void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection? blockSel,
        EntitySelection? entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling,
        ref EnumHandling handling)
    {
        // set default handling
        handling = EnumHandling.PassThrough;
        handHandling = EnumHandHandling.NotHandled;

        // guard clause - ignition tools disabled
        if (!VanillaExpandedModSystem.Config.EnableIgnitionTools)
        {
            return;
        }

        // guard clause - block selection must be valid
        if (blockSel is null)
        {
            return;
        }
        Block block = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;

        // guard clause - must have access to use the block
        if (byPlayer is null || !byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
        {
            return;
        }

        // guard clause - block must be ignitable
        if (block is not IIgnitable ign)
        {
            return;
        }

        EnumIgniteState state = ign.OnTryIgniteBlock(byEntity, blockSel.Position, 0f);
        switch (state)
        {
            case EnumIgniteState.Ignitable:
                {
                    // Success, we are beginning the ignition process.
                    handling = EnumHandling.PreventDefault;
                    handHandling = EnumHandHandling.PreventDefault;
                    if (igniteAnimation is not null)
                    {
                        byEntity.AnimManager.StartAnimation(igniteAnimation);
                    }
                    break;
                }
            case EnumIgniteState.NotIgnitablePreventDefault:
                {
                    handHandling = EnumHandHandling.PreventDefault;
                    break;
                }
            default:
                {
                    break;
                }
        }
    }

    public bool OnHeldInteractStep(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection? blockSel,
        EntitySelection? entitySel,
        ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
        if (blockSel is null)
        {
            CancelIgnitionAttempt(byEntity);
            return false;
        }
        IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;
        if (!byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
        {
            CancelIgnitionAttempt(byEntity);
            return false;
        }
        Block block = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        EnumIgniteState igniteState = EnumIgniteState.NotIgnitable;
        if (block is IIgnitable ign)
        {
            igniteState = ign.OnTryIgniteBlock(byEntity, blockSel.Position, secondsUsed);
        }

        switch (igniteState)
        {
            case EnumIgniteState.Ignitable:
                {
                    handling = EnumHandling.PreventDefault;
                    if (byEntity.World is IClientWorldAccessor)
                    {
                        bool cycle = (int)(secondsUsed * 30.0) % 2 == 1;
                        if (secondsUsed > IgnitionDelay && cycle)
                        {
                            Random rand = byEntity.World.Rand;
                            const double quarterBlock = 0.125;
                            const double halfBlock = 0.25;
                            double offsetX = rand.NextDouble() * halfBlock - quarterBlock;
                            double offsetY = rand.NextDouble() * halfBlock - quarterBlock;
                            double offsetZ = rand.NextDouble() * halfBlock - quarterBlock;
                            Vec3d pos = blockSel.Position.ToVec3d().Add(blockSel.HitPosition).Add(offsetX, offsetY, offsetZ);
                            Block blockFire = byEntity.World.GetBlock(new AssetLocation("game", "fire"));
                            AdvancedParticleProperties props = blockFire.ParticleProperties[blockFire.ParticleProperties.Length - 1].Clone();
                            props.basePos = pos;
                            props.Quantity.avg = 0.5f;
                            byEntity.World.SpawnParticles(props);
                            props.Quantity.avg = 0f;
                        }
                    }

                    return true;
                }
            case EnumIgniteState.IgniteNow:
                {
                    handling = EnumHandling.PreventDefault;
                    return false;
                }
            case EnumIgniteState.NotIgnitable:
            case EnumIgniteState.NotIgnitablePreventDefault:
                {
                    handling = EnumHandling.PassThrough;
                    CancelIgnitionAttempt(byEntity);
                    return false;
                }
            default:
                return false;
        }
    }

    public void OnHeldInteractStop(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection? blockSel,
        EntitySelection? entitySel,
        ref EnumHandling handling)
    {
        byEntity.AnimManager.StopAnimation(igniteAnimation);
        if (blockSel is null)
        {
            return;
        }
        Block block = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
        if (block is null)
        {
            return;
        }
        IIgnitable? ign = block as IIgnitable;
        EnumIgniteState igniteState = ign?.OnTryIgniteBlock(byEntity, blockSel.Position, secondsUsed) ?? EnumIgniteState.NotIgnitable;

        if (igniteState != EnumIgniteState.IgniteNow)
        {
            api?.Event.UnregisterCallback(ObjectCacheUtil.TryGet<long>(api, SoundId));
            return;
        }

        IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;
        if (byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
        {
            EnumHandling handled = EnumHandling.PassThrough;
            ign?.OnTryIgniteBlockOver(byEntity, blockSel.Position, secondsUsed, ref handled);
        }
    }

    public bool OnHeldInteractCancel(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection? blockSel,
        EntitySelection? entitySel,
        EnumItemUseCancelReason cancelReason,
        ref EnumHandling handled)
    {
        CancelIgnitionAttempt(byEntity);
        return true;
    }

    private void CancelIgnitionAttempt(EntityAgent byEntity)
    {
        api?.Event.UnregisterCallback(ObjectCacheUtil.TryGet<long>(api, SoundId));
        byEntity.StopAnimation(igniteAnimation);
    }
    #endregion
}
