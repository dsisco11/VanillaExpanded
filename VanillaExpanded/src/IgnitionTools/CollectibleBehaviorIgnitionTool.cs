using Vintagestory.API.Common;

namespace VanillaExpanded.IgnitionTools;

/// <summary>
/// Collectible behavior for items that can ignite ignitable blocks.
/// Forwards all interaction handling to <see cref="IgnitionToolLogic"/>.
/// </summary>
public sealed class CollectibleBehaviorIgnitionTool : CollectibleBehavior
{
    #region Constants
    public static string RegistryId => "IgnitionTool";
    #endregion

    #region Fields
    private IgnitionToolLogic? logic;
    #endregion

    #region Initialization
    public CollectibleBehaviorIgnitionTool(CollectibleObject item) : base(item)
    {
        /*
         * Set HeldPriorityInteract=true on the block.
         * This ensures the held item's OnHeldInteractStart runs before the block's OnBlockInteractStart
         * when sneaking, allowing the ignition behavior to prevent block interactions like the bloomery's item insertion from consuming the click.
         */
        item.HeldPriorityInteract = true;
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        logic = new IgnitionToolLogic(api);
    }
    #endregion

    #region Item Interaction
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling,
        ref EnumHandling handling)
    {
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);
        logic?.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);
    }

    public override bool OnHeldInteractStep(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandling handling)
    {
        if (logic is null)
        {
            return base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);
        }
        return logic.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);
    }

    public override void OnHeldInteractStop(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        ref EnumHandling handling)
    {
        logic?.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);
    }

    public override bool OnHeldInteractCancel(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumItemUseCancelReason cancelReason,
        ref EnumHandling handled)
    {
        return logic?.OnHeldInteractCancel(secondsUsed, slot, byEntity, blockSel, entitySel, cancelReason, ref handled) ?? true;
    }
    #endregion
}
