using VanillaExpanded.Network;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VanillaExpanded.SpawnDecal;

/// <summary>
/// Server-side mod system for tracking and syncing spawn positions.
/// </summary>
public class SpawnDecalServerSystem : ModSystem
{
    #region Fields
    private ICoreServerAPI? sapi;
    private IServerNetworkChannel? channel;
    #endregion

    #region ModSystem Overrides
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;

        // Get the network channel
        channel = api.Network.GetChannel(Mod.Info.ModID);

        // Subscribe to player events
        api.Event.PlayerJoin += OnPlayerJoin;
    }

    public override void Dispose()
    {
        if (sapi != null)
        {
            sapi.Event.PlayerJoin -= OnPlayerJoin;
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
    public void OnSpawnPositionSet(IServerPlayer player, PlayerSpawnPos pos)
    {
        SendSpawnUpdate(player, pos);
    }

    /// <summary>
    /// Called from Harmony patch when a player's spawn position is cleared.
    /// </summary>
    public void OnSpawnPositionCleared(IServerPlayer player)
    {
        SendSpawnCleared(player);
    }
    #endregion

    #region Event Handlers
    private void OnPlayerJoin(IServerPlayer player)
    {
        // Sync current spawn position to the joining player
        var spawnPos = player.GetSpawnPosition(false);
        if (spawnPos != null)
        {
            SendSpawnUpdate(player, spawnPos);
        }
        else
        {
            SendSpawnCleared(player);
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
