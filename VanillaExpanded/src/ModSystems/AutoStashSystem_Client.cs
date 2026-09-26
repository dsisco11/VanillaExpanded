using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VanillaExpanded.AutoStashing;

namespace VanillaExpanded;

internal class AutoStashSystem_Client : ModSystem
{
    #region Fields
    private ICoreClientAPI? api;
    private IClientNetworkChannel? channel;
    internal EntityAttachedContainerAutoStashClient? EntityAttachedContainers { get; private set; }
    #endregion

    #region Accessors
    protected ILogger Logger => api!.Logger;
    #endregion

    #region Hooks
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client && VanillaExpandedModSystem.Config.EnableAutoStash;
    }

    public override void Dispose()
    {
        EntityAttachedContainers?.Dispose();
        EntityAttachedContainers = null;
        base.Dispose();
        api = null;
        channel = null;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        channel = api.Network.GetChannel(Constants.ModId);
        EntityAttachedContainers = new EntityAttachedContainerAutoStashClient(api, RequestEntityAutoStash);
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

    public void RequestEntityAutoStash(long entityId, int attachmentSlotIndex)
    {
        if (channel is null)
        {
            Logger.Error("Cannot send entity auto-stash request packet: Network channel is null.");
            return;
        }

        channel.SendPacket(new Network.Packet_RequestEntityAutoStash
        {
            EntityId = entityId,
            AttachmentSlotIndex = attachmentSlotIndex
        });
    }

    #endregion
}
