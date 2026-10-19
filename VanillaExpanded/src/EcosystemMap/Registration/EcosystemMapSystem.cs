using System;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Registration;

/// <summary>Registers the shared layer identity and composes feature-owned acquisition lifetimes on each side.</summary>
public sealed class EcosystemMapSystem : ModSystem
{
    internal const string LayerCode = "vanillaexpanded:ecosystem";
    private ICoreClientAPI? clientApi;
    private ClientReceiptAdapter? receipt;
    private IClientNetworkChannel? cacheResetChannel;
    private ServerShrubSource? server;
    internal EcosystemClientSession? Client { get; private set; }
    internal event Action? Stopping;

    #region Public API
    #region Registration and side initialization
    /// <summary>Runs after the vanilla map manager's registration setup and before world finalization.</summary>
    public override double ExecuteOrder() => 0.3;

    /// <summary>Registers the exact same code/type on both sides for vanilla envelope dispatch.</summary>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.ModLoader.GetModSystem<WorldMapManager>().RegisterMapLayer<EcosystemMapLayer>(LayerCode, 1.1);
    }

    /// <summary>Installs dictionary synchronization before packet receipt; storage opens when world identity is ready.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        try
        {
            cacheResetChannel = api.Network.RegisterChannel(EcosystemCacheReset.ChannelName).RegisterMessageType<EcosystemCacheReset>();
            receipt = new ClientReceiptAdapter(api.World as ClientMain
                ?? throw new NotSupportedException("Client world implementation changed."),
                error => api.Logger.Error("Ecosystem receipt failed: {0}", error));
            api.Event.LeaveWorld += StopWorld;
        }
        catch (Exception error) { api.Logger.Error("Ecosystem acquisition unavailable: {0}", error); }
    }

    /// <summary>Observes server ordinary-send obligations before the layer and playing clients are initialized.</summary>
    public override void StartServerSide(ICoreServerAPI api)
    {
        try { server = new ServerShrubSource(api); }
        catch (Exception error) { api.Logger.Error("Ecosystem shrub support unavailable: {0}", error); }
    }

    #endregion
    #region Layer and world lifetime
    /// <summary>Stops both world lifetimes and removes this feature's listener/patch ownership.</summary>
    public override void Dispose()
    {
        try { StopWorld(); }
        finally { base.Dispose(); }
    }

    /// <summary>Composes the client cache or binds server delivery once the registered layer exists.</summary>
    internal void LoadLayer(EcosystemMapLayer layer)
    {
        server?.Bind(layer);
        if (clientApi == null || receipt == null || Client != null) return;
        try
        {
            Client = EcosystemClientSession.Open(clientApi, receipt);
            Client.Purged += NotifyServerCacheReset;
        }
        catch (Exception error)
        {
            receipt.Dispose();
            receipt = null;
            clientApi.Logger.Error("Ecosystem storage unavailable: {0}", error);
        }
    }

    /// <summary>Detaches receipt hooks before draining the client session; leave-world ordering and repeated disposal are harmless.</summary>
    internal void StopWorld()
    {
        if (clientApi != null) clientApi.Event.LeaveWorld -= StopWorld;
        // Session detaches and joins ocean intake before stopping receipt snapshots and draining durable writes.
        try
        {
            try { Stopping?.Invoke(); }
            finally { Client?.Dispose(); }
        }
        finally
        {
            Client = null;
            try { receipt?.Dispose(); receipt = null; }
            finally { server?.Dispose(); server = null; cacheResetChannel = null; clientApi = null; }
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Invalidates server revision suppression only when its registered optional channel confirms ecosystem support.</summary>
    private void NotifyServerCacheReset()
    {
        if (cacheResetChannel?.Connected == true) cacheResetChannel.SendPacket(new EcosystemCacheReset());
    }
    #endregion
}
