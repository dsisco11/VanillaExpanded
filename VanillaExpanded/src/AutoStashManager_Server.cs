using VanillaExpanded.AutoStashing;
using VanillaExpanded.Network;

using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VanillaExpanded.src;

internal class AutoStashManager_Server : AutoStashManager
{
    #region Fields
    private ICoreServerAPI api => (ICoreServerAPI)coreApi;
    protected readonly IServerNetworkChannel channel;
    #endregion

    #region Constructors
    public AutoStashManager_Server(in ModInfo mod, ICoreServerAPI api) : base(mod, api)
    {
        channel = api.Network.GetChannel(mod.ModID);
        channel.SetMessageHandler<Packet_RequestAutoStash>(ProcessAutoStashRequest);
    }
    #endregion

    #region Network Handlers
    private void ProcessAutoStashRequest(IServerPlayer fromPlayer, Packet_RequestAutoStash packet)
    {
        // find the block at the requested position
        var block = api.World.BlockAccessor.GetBlock(packet.position);
        // get the "AutoStashable" behavior for the block
        var autoStashBehavior = block.GetBehavior<BlockBehaviorAutoStashable>();
        if (autoStashBehavior is not null)
        {
            autoStashBehavior.TryStashInventory(api.World, fromPlayer, packet.position.Copy());
        }
    }
    #endregion
}
