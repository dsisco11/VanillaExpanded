using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock implementation of IServerNetworkChannel for testing server-side network communication.
/// Captures sent/broadcast packets and allows verification of network operations.
/// </summary>
public class MockServerNetworkChannel : IServerNetworkChannel
{
    private readonly Dictionary<Type, Delegate> _messageHandlers = [];
    private readonly List<(object Packet, IServerPlayer[] Players)> _sentPackets = [];
    private readonly List<(object Packet, IServerPlayer[] ExceptPlayers)> _broadcastPackets = [];
    private readonly List<Type> _registeredTypes = [];

    /// <summary>
    /// Gets the list of packets that have been sent to specific players.
    /// </summary>
    public IReadOnlyList<(object Packet, IServerPlayer[] Players)> SentPackets => _sentPackets;

    /// <summary>
    /// Gets the list of packets that have been broadcast.
    /// </summary>
    public IReadOnlyList<(object Packet, IServerPlayer[] ExceptPlayers)> BroadcastPackets => _broadcastPackets;

    /// <summary>
    /// Gets the list of message types that have been registered.
    /// </summary>
    public IReadOnlyList<Type> RegisteredTypes => _registeredTypes;

    /// <inheritdoc />
    public string ChannelName { get; }

    public MockServerNetworkChannel(string channelName = "test-channel")
    {
        ChannelName = channelName;
    }

    /// <inheritdoc />
    public IServerNetworkChannel RegisterMessageType(Type type)
    {
        _registeredTypes.Add(type);
        return this;
    }

    /// <inheritdoc />
    public IServerNetworkChannel RegisterMessageType<T>()
    {
        return RegisterMessageType(typeof(T));
    }

    /// <inheritdoc />
    INetworkChannel INetworkChannel.RegisterMessageType(Type type)
    {
        return RegisterMessageType(type);
    }

    /// <inheritdoc />
    INetworkChannel INetworkChannel.RegisterMessageType<T>()
    {
        return RegisterMessageType<T>();
    }

    /// <inheritdoc />
    public IServerNetworkChannel SetMessageHandler<T>(NetworkClientMessageHandler<T> messageHandler)
    {
        _messageHandlers[typeof(T)] = messageHandler;
        return this;
    }

    /// <inheritdoc />
    public void SendPacket<T>(T message, params IServerPlayer[] players)
    {
        if (message is not null)
        {
            _sentPackets.Add((message, players));
        }
    }

    /// <inheritdoc />
    public void SendPacket<T>(T message, byte[] data, params IServerPlayer[] players)
    {
        SendPacket(message, players);
    }

    /// <inheritdoc />
    public void BroadcastPacket<T>(T message, params IServerPlayer[] exceptPlayers)
    {
        if (message is not null)
        {
            _broadcastPackets.Add((message, exceptPlayers));
        }
    }

    /// <summary>
    /// Simulates receiving a packet from a client player, invoking the registered handler.
    /// </summary>
    public void SimulateReceivePacket<T>(IServerPlayer fromPlayer, T packet)
    {
        if (_messageHandlers.TryGetValue(typeof(T), out var handler))
        {
            ((NetworkClientMessageHandler<T>)handler)(fromPlayer, packet);
        }
    }

    /// <summary>
    /// Gets the last packet sent to players of a specific type.
    /// </summary>
    public T? GetLastSentPacket<T>() where T : class
    {
        return _sentPackets
            .Select(p => p.Packet)
            .OfType<T>()
            .LastOrDefault();
    }

    /// <summary>
    /// Gets the last broadcast packet of a specific type.
    /// </summary>
    public T? GetLastBroadcastPacket<T>() where T : class
    {
        return _broadcastPackets
            .Select(p => p.Packet)
            .OfType<T>()
            .LastOrDefault();
    }

    /// <summary>
    /// Clears all sent and broadcast packets.
    /// </summary>
    public void ClearPackets()
    {
        _sentPackets.Clear();
        _broadcastPackets.Clear();
    }

    /// <summary>
    /// Verifies that a packet of the specified type was sent.
    /// </summary>
    public bool WasPacketSent<T>()
    {
        return _sentPackets.Any(p => p.Packet is T);
    }

    /// <summary>
    /// Verifies that a packet of the specified type was broadcast.
    /// </summary>
    public bool WasPacketBroadcast<T>()
    {
        return _broadcastPackets.Any(p => p.Packet is T);
    }
}
