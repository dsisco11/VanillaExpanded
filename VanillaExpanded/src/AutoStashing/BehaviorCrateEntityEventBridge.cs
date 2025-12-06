using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.src.AutoStashing;
/// <summary>
/// Forwards the "OnBlockInteract" events to the BlockCrateEntity class, so that we can actually make the Crate entities propagate behavior events correctly.
/// This is needed since the devs don't use their own systems in a consistent manner, and for some reason they put all of the "Crate Container Entity" player interaction logic into the block-entity class instead of an actual block behavior class...
/// </summary>
public class BehaviorCrateEntityEventBridge : BlockBehavior
{
    #region Constants
    public static string RegistryId => "CrateEntityEventBridge";
    #endregion

    public BehaviorCrateEntityEventBridge(Block block) : base(block)
    {
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        handling = EnumHandling.PreventSubsequent;
        BlockEntityCrate be = world.BlockAccessor.GetBlockEntity<BlockEntityCrate>(blockSel.Position);
        return be?.OnBlockInteractStart(byPlayer, blockSel) ?? false;
    }
}
