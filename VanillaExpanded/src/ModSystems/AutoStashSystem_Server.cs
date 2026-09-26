using VanillaExpanded.AutoStashing;
using VanillaExpanded.Network;
using VanillaExpanded.src.AutoStashing;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace VanillaExpanded.src.ModSystems;

internal class AutoStashSystem_Server : ModSystem
{
    #region Fields
    protected ICoreServerAPI? api;
    protected IServerNetworkChannel? channel;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Server && VanillaExpandedModSystem.Config.EnableAutoStash;
    }

    public override void Dispose()
    {
        base.Dispose();
        channel = null;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        channel = api.Network.GetChannel(Constants.ModId);
        channel.SetMessageHandler<Packet_RequestAutoStash>(ProcessAutoStashRequest);
        channel.SetMessageHandler<Packet_RequestEntityAutoStash>(ProcessEntityAutoStashRequest);
    }
    #endregion

    #region Network Handlers
    private void ProcessAutoStashRequest(IServerPlayer fromPlayer, Packet_RequestAutoStash packet)
    {
        if (packet.position is null)
        {
            api!.Logger.Warning("[AutoStash] Ignoring request with no target position.");
            return;
        }

        api!.Logger.Audit("[AutoStash] Processing auto-stash request from client '{0}' (uid: {1})", fromPlayer.PlayerName, fromPlayer.PlayerUID);

    #pragma warning disable CS0618 // Vintage Story marks this for a planned 1.23 signature change.
        var permissions = new BlockEntity.CachedAccessPerms(api.World, packet.position, fromPlayer);
    #pragma warning restore CS0618
        if (!permissions.IsInteractingPlayerAllowedTo(EnumBlockAccessFlags.Use, true, "auto-stash container"))
        {
            return;
        }

        // find the block at the requested position
        var block = api!.World.BlockAccessor.GetBlock(packet.position);
        // get the "AutoStashable" behavior for the block
        var autoStashBehavior = block.GetBehavior<BlockBehaviorAutoStashable>();
        autoStashBehavior?.TryStashPlayerInventory(api.World, fromPlayer, packet.position.Copy());
    }

    private void ProcessEntityAutoStashRequest(IServerPlayer fromPlayer, Packet_RequestEntityAutoStash packet)
    {
        Entity? entity = api!.World.GetEntityById(packet.EntityId);
        EntityBehaviorAttachable? attachable = entity?.GetBehavior<EntityBehaviorAttachable>();
        if (entity is null || attachable is null || fromPlayer.Entity.Pos.SquareDistanceTo(entity.Pos) > 36)
        {
            return;
        }

        EntityAttachedContainerAutoStash.TryAutoStash(
            api.World,
            fromPlayer,
            entity,
            attachable,
            packet.AttachmentSlotIndex);
    }
    #endregion
}
