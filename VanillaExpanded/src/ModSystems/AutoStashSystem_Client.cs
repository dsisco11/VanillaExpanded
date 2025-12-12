using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded;

internal class AutoStashSystem_Client : ModSystem
{
    #region Fields
    private ICoreClientAPI? api;
    private IClientNetworkChannel? channel;
    #endregion

    #region Accessors
    protected ILogger Logger => api!.Logger;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
    public override void Dispose()
    {
        base.Dispose();
        api = null;
        channel = null;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        channel = api.Network.GetChannel(Mod.Info.ModID);
    }
    #endregion

    #region Network Handlers
    public void RequestAutoStash(in BlockPos pos)
    {
        if (channel is null)
        {
            Logger.Error("Cannot send auto-stash request packet: Network channel is null.");
            return;
        }

        var packet = new Network.Packet_RequestAutoStash()
        {
            position = pos.Copy()
        };
        channel?.SendPacket(packet);
    }
    #endregion
}
