using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.RadialProgress;

namespace VanillaExpanded;

/// <summary>Owns AutoStash client packet dispatch and the attached-container gesture controller.</summary>
internal class AutoStashSystem_Client : ModSystem
{
    private readonly IProgressSystemProvider? progressSystem;
    private ICoreClientAPI? api;
    private IClientNetworkChannel? channel;
    internal EntityAttachedContainerAutoStashClient? EntityAttachedContainers { get; private set; }
    protected ILogger Logger => api!.Logger;

    #region Public API
    #region Construction
    /// <summary>Creates the client system with the existing progress manager.</summary>
    public AutoStashSystem_Client() { }

    /// <summary>Supplies the progress ownership service to the attached gesture controller.</summary>
    internal AutoStashSystem_Client(IProgressSystemProvider progressSystem)
    {
        this.progressSystem = progressSystem;
    }
    #endregion

    #region Hooks
    /// <summary>Loads the client request boundary only when AutoStash is enabled.</summary>
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client && VanillaExpandedModSystem.Config.EnableAutoStash;
    }

    /// <summary>Releases the gesture controller before discarding client API and channel references.</summary>
    public override void Dispose()
    {
        EntityAttachedContainers?.Dispose();
        EntityAttachedContainers = null;
        base.Dispose();
        api = null;
        channel = null;
    }

    /// <summary>Resolves the channel and installs the attached gesture controller with its progress boundary.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        channel = api.Network.GetChannel(Constants.ModId);
        EntityAttachedContainers = new EntityAttachedContainerAutoStashClient(api, RequestEntityAutoStash, progressSystem);
    }
    #endregion

    #region Network Handlers
    /// <summary>Sends a block request with an independent copy of the selected position.</summary>
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

    /// <summary>Sends the selected attached entity and inventory slot to the server.</summary>
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
    #endregion
}
