using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Transport;

/// <summary>Tracks ordinary sent-region obligations per connection and deduplicates original shrub revisions.</summary>
internal sealed class ShrubDelivery<TConnection> where TConnection : class
{
    private readonly Dictionary<TConnection, ConnectionState> connections = new(ReferenceEqualityComparer.Instance);

    #region Public API
    #region Connection observations
    /// <summary>Records a normal region delivery even before the layer or player is ready.</summary>
    internal void Observe(TConnection connection, EcosystemRegionKey key)
    {
        if (!connections.TryGetValue(connection, out var state)) connections[connection] = state = new();
        state.Pending.Add(key);
    }

    /// <summary>Immediately forgets one disconnected connection, including obligations observed before the first tick.</summary>
    internal void Forget(TConnection connection) => connections.Remove(connection);

    /// <summary>Removes disconnected generations, preventing their pending data or deduplication from entering a reconnect.</summary>
    internal void RetainConnections(IEnumerable<TConnection> online)
    {
        var retained = new HashSet<TConnection>(online, ReferenceEqualityComparer.Instance);
        foreach (var connection in connections.Keys.Where(key => !retained.Contains(key)).ToArray()) connections.Remove(connection);
    }

    #endregion
    #region Delivery and world cleanup
    /// <summary>Drains only already-observed regions via a loaded-only reader; unavailable inputs await a subsequent ordinary send observation.</summary>
    internal void Drain(TConnection connection, bool ready, Func<EcosystemRegionKey, EnvironmentalMap?> readLoaded,
        Action<byte[]> send)
    {
        if (!ready || !connections.TryGetValue(connection, out var state)) return;
        foreach (var key in state.Pending.ToArray())
        {
            EnvironmentalMap? map = readLoaded(key);
            if (map == null) { state.Pending.Remove(key); continue; }
            ulong revision = ShrubRevision.Compute(key, map);
            // Retain only a 64-bit content hash per sent region, rather than duplicating arrays per player.
            if (!state.Sent.TryGetValue(key, out var previous) || revision != previous)
            {
                send(ShrubPayload.Encode(key, map));
                state.Sent[key] = revision;
            }
            state.Pending.Remove(key);
        }
    }

    /// <summary>Clears all connection-specific obligations and revision comparisons on world shutdown.</summary>
    internal void Clear() => connections.Clear();
    #endregion
    #endregion

    #region Private
    /// <summary>Owns one connection generation's pending keys and last successfully transmitted content hashes.</summary>
    private sealed class ConnectionState
    {
        internal HashSet<EcosystemRegionKey> Pending { get; } = new();
        internal Dictionary<EcosystemRegionKey, ulong> Sent { get; } = new();
    }
    #endregion
}
