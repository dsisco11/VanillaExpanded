using System;

using VanillaExpanded.Network;

using Vintagestory;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VanillaExpanded.SpawnDecal;

/// <summary>
/// Server-side mod system for tracking and syncing spawn positions.
/// </summary>
public class SpawnDecalServerSystem : ModSystem
{
    #region Singleton Instance
    public static SpawnDecalServerSystem? Instance { get; private set; }
    #endregion

    #region Fields
    private ICoreServerAPI? sapi;
    private IServerNetworkChannel? channel;
    #endregion

    #region Accessors
    public ILogger Logger => sapi?.Logger ?? throw new InvalidOperationException("Server API not initialized.");
    #endregion

    #region ModSystem Overrides
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        Instance = this;

        // Get the network channel
        channel = api.Network.GetChannel(Mod.Info.ModID);

        // Subscribe to player events - use PlayerNowPlaying to ensure client is ready to receive packets
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        api.Event.PlayerRespawn += Event_PlayerRespawn;
    }

    public override void Dispose()
    {
        if (sapi is not null)
        {
            sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
        }

        sapi = null;
        channel = null;
        base.Dispose();
    }
    #endregion

    #region Static Methods for Harmony Patches
    /// <summary>
    /// Called from Harmony patch when a player's spawn position is set.
    /// </summary>
    public static void OnSpawnPositionSet(IServerPlayer player, PlayerSpawnPos pos)
    {
        Instance.Logger.Audit($"Player '{player.PlayerName}' set spawn position to ({pos.x}, {pos.y}, {pos.z})");
        Instance.SendSpawnUpdate(player, pos);
    }

    /// <summary>
    /// Called from Harmony patch when a player's spawn position is cleared.
    /// </summary>
    public static void OnSpawnPositionCleared(IServerPlayer player)
    {
        Instance.Logger.Audit($"Player '{player.PlayerName}' cleared spawn position");
        Instance.SendSpawnCleared(player);
    }
    #endregion

    #region Event Handlers
    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        Logger.Audit($"Player '{player.PlayerName}' is now playing - syncing spawn position");
        // Sync current spawn position to the joining player once they're fully loaded
        var spawnPos = player.GetSpawnPosition(false);
        if (spawnPos is not null)
        {
            SendSpawnUpdate(player, spawnPos);
        }
        else
        {
            SendSpawnCleared(player);
        }
    }

    private void Event_PlayerRespawn(IServerPlayer byPlayer)
    {
        Logger.Audit($"Player '{byPlayer.PlayerName}' has respawned - syncing spawn position");
        // Sync current spawn position to the respawning player
        var spawnPos = byPlayer.GetSpawnPosition(false);
        if (spawnPos is not null)
        {
            SendSpawnUpdate(byPlayer, spawnPos);
        }
        else
        {
            SendSpawnCleared(byPlayer);
        }
    }
    #endregion

    #region Network Methods
    private void SendSpawnUpdate(IServerPlayer player, PlayerSpawnPos pos)
    {
        if (channel == null || sapi == null)
            return;

        var packet = new Packet_TemporalSpawn
        {
            HasSpawn = true,
            X = pos.x + 0.5, // Center of block
            Y = pos.y ?? 0,
            Z = pos.z + 0.5  // Center of block
        };

        channel.SendPacket(packet, player);
    }

    private void SendSpawnUpdate(IServerPlayer player, FuzzyEntityPos pos)
    {
        if (channel == null || sapi == null)
            return;

        var packet = new Packet_TemporalSpawn
        {
            HasSpawn = true,
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z
        };

        channel.SendPacket(packet, player);
    }

    private void SendSpawnCleared(IServerPlayer player)
    {
        if (channel == null)
            return;

        var packet = new Packet_TemporalSpawn
        {
            HasSpawn = false,
            X = 0,
            Y = 0,
            Z = 0
        };

        channel.SendPacket(packet, player);
    }
    #endregion
}
