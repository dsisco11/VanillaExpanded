using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.src;

internal class AutoStashManager_Client : AutoStashManager
{
    #region Fields
    private ICoreClientAPI api => (ICoreClientAPI)coreApi;
    private readonly IClientNetworkChannel channel;
    #endregion

    #region Constructors
    public AutoStashManager_Client(in ModInfo mod, ICoreClientAPI api) : base(mod, api)
    {
        channel = api.Network.GetChannel(mod.ModID);
    }
    #endregion

    public void RequestAutoStash(in BlockPos pos)
    {
        var packet = new Network.Packet_RequestAutoStash()
        {
            position = pos.Copy()
        };
        channel.SendPacket(packet);
    }

    #region Network Handlers
    #endregion
}
