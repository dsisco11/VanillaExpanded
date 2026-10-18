using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.EcosystemMap.Transport;

/// <summary>Observes normal region delivery on the server owner thread and snapshots loaded original shrubs for map-layer routing.</summary>
internal sealed class ServerShrubSource : IDisposable
{
    private const string PatchId = "vanillaexpanded.ecosystem.servershrubs";
    private static ServerShrubSource? active;
    private readonly ICoreServerAPI api;
    private readonly ServerMain server;
    private readonly Harmony harmony = new(PatchId);
    private readonly ShrubDelivery<ConnectedClient> delivery = new();
    private readonly HashSet<ConnectedClient> seeded = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ConnectedClient> disconnected = new(ReferenceEqualityComparer.Instance);
    private readonly long tick;
    private EcosystemMapLayer? layer;
    private bool disposed;

    #region Public API
    /// <summary>Installs normal-send observation before layer initialization and retains early obligations until the player is playing.</summary>
    internal ServerShrubSource(ICoreServerAPI api)
    {
        this.api = api;
        server = api.World as ServerMain ?? throw new NotSupportedException("Server world implementation changed.");
        if (Interlocked.CompareExchange(ref active, this, null) != null)
            throw new InvalidOperationException("An ecosystem shrub source is already installed.");
        try
        {
            var method = AccessTools.DeclaredMethod(typeof(ConnectedClient), nameof(ConnectedClient.SetMapRegionSent), new[] { typeof(long) })
                ?? throw new NotSupportedException("Ordinary region-send notification changed.");
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(ServerShrubSource), nameof(RegionSent)));
            tick = api.Event.RegisterGameTickListener(Drain, 50);
            api.Event.PlayerDisconnect += OnDisconnect;
            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Dispose);
            api.Network.RegisterChannel(EcosystemCacheReset.ChannelName).RegisterMessageType<EcosystemCacheReset>()
                .SetMessageHandler<EcosystemCacheReset>(OnCacheReset);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Enables the exact layer type registered with WorldMapManager; early obligations are drained by the owner-thread tick.</summary>
    internal void Bind(EcosystemMapLayer mapLayer) => layer = mapLayer;

    /// <summary>Stops ordinary delivery, removes owned patches/listeners and drops every connection generation's pending state.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interlocked.CompareExchange(ref active, null, this);
        harmony.UnpatchAll(PatchId);
        if (tick != 0) api.Event.UnregisterGameTickListener(tick);
        api.Event.PlayerDisconnect -= OnDisconnect;
        delivery.Clear();
        seeded.Clear();
        disconnected.Clear();
        layer = null;
    }
    #endregion

    #region Private
    /// <summary>Observes only connections belonging to this server, preserving identity rather than trusting payload-supplied player ids.</summary>
    private static void RegionSent(ConnectedClient __instance, long index2d)
    {
        // Undo signed X before shifting: vanilla's regional index uses addition, not bitwise packing.
        var source = Volatile.Read(ref active);
        if (source != null && !source.disconnected.Contains(__instance) && source.server.Clients.TryGetValue(__instance.Id, out var current) && ReferenceEquals(current, __instance))
            source.delivery.Observe(__instance, new((int)index2d, unchecked((int)((index2d - (int)index2d) >> 32))));
    }

    /// <summary>Clears exact player connection generations on disconnect, including early sends not yet seeded by the tick.</summary>
    private void OnDisconnect(IServerPlayer player)
    {
        foreach (var connection in server.Clients.Values.Concat(seeded).Where(value => ReferenceEquals(value.Player, player)).Distinct().ToArray())
        {
            delivery.Forget(connection);
            seeded.Remove(connection);
            disconnected.Add(connection);
        }
    }

    /// <summary>Uses authenticated transport identity to invalidate only the sender's previously transmitted shrub revisions.</summary>
    private void OnCacheReset(IServerPlayer sender, EcosystemCacheReset message)
    {
        if (disposed || message.Version != 1) return;
        foreach (var connection in server.Clients.Values.Where(value => ReferenceEquals(value.Player, sender) && !disconnected.Contains(value)))
        {
            delivery.Forget(connection);
            // Reseed already-sent regions to cover ordinary observations that raced the notification; reads remain loaded-only.
            seeded.Remove(connection);
        }
    }

    /// <summary>Seeds already-sent loaded coverage and sends only from the owner thread after player/layer initialization.</summary>
    private void Drain(float dt)
    {
        if (disposed) return;
        var present = server.Clients.Values.ToArray();
        disconnected.IntersectWith(present);
        var online = present.Where(connection => !disconnected.Contains(connection)).ToArray();
        delivery.RetainConnections(online);
        seeded.IntersectWith(online);
        foreach (var connection in online)
        {
            // Inspect only the engine's already-sent set, never a requested or generated region range.
            if (seeded.Add(connection))
                foreach (long index in connection.MapRegionSent)
                    delivery.Observe(connection, new((int)index, unchecked((int)((index - (int)index) >> 32))));
            if (layer == null) continue;
            try
            {
                delivery.Drain(connection, connection.IsPlayingClient && connection.Player != null,
                    key => EnvironmentalSnapshots.Map(api.WorldManager.GetMapRegion(key.X, key.Z)?.ShrubMap),
                    payload => api.ModLoader.GetModSystem<WorldMapManager>().SendMapDataToClient(layer, connection.Player, payload));
            }
            catch (Exception error) { api.Logger.Error("Ecosystem shrub delivery failed: {0}", error); }
        }
    }
    #endregion
}
