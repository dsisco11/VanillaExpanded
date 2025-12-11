using VanillaExpanded.AutoStashing;
using VanillaExpanded.Network;
using VanillaExpanded.src.AutoStashing;

using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VanillaExpanded.src.ModSystems;

internal class AutoStashSystem_Server : ModSystem
{
    #region Fields
    protected ICoreServerAPI? api;
    protected IServerNetworkChannel? channel;
    #endregion

    #region Hooks
    public override void Dispose()
    {
        base.Dispose();
        channel = null;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        channel = api.Network.GetChannel(Mod.Info.ModID);
        channel.SetMessageHandler<Packet_RequestAutoStash>(ProcessAutoStashRequest);
    }
    #endregion

    #region Network Handlers
    private void ProcessAutoStashRequest(IServerPlayer fromPlayer, Packet_RequestAutoStash packet)
    {
        api!.Logger.Audit("[AutoStash] Processing auto-stash request from client '{0}' (uid: {1})", fromPlayer.PlayerName, fromPlayer.PlayerUID);
        // find the block at the requested position
        var block = api!.World.BlockAccessor.GetBlock(packet.position);
        // get the "AutoStashable" behavior for the block
        var autoStashBehavior = block.GetBehavior<BlockBehaviorAutoStashable>();
        autoStashBehavior?.TryStashPlayerInventory(api.World, fromPlayer, packet.position.Copy());
    }
    #endregion
}
