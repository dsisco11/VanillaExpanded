using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock implementation of IClientNetworkChannel for testing client-side network communication.
/// Captures sent packets and allows verification of network operations.
/// </summary>
public class MockClientNetworkChannel : IClientNetworkChannel
{
    private readonly Dictionary<Type, Delegate> _messageHandlers = [];
    private readonly List<object> _sentPackets = [];
    private readonly List<Type> _registeredTypes = [];

    /// <summary>
    /// Gets the list of packets that have been sent through this channel.
    /// </summary>
    public IReadOnlyList<object> SentPackets => _sentPackets;

    /// <summary>
    /// Gets the list of message types that have been registered.
    /// </summary>
    public IReadOnlyList<Type> RegisteredTypes => _registeredTypes;

    /// <inheritdoc />
    public string ChannelName { get; }

    /// <inheritdoc />
    public bool Connected { get; set; } = true;

    public MockClientNetworkChannel(string channelName = "test-channel")
    {
        ChannelName = channelName;
    }

    /// <inheritdoc />
    public IClientNetworkChannel RegisterMessageType(Type type)
    {
        _registeredTypes.Add(type);
        return this;
    }

    /// <inheritdoc />
    public IClientNetworkChannel RegisterMessageType<T>()
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
    public IClientNetworkChannel SetMessageHandler<T>(NetworkServerMessageHandler<T> handler)
    {
        _messageHandlers[typeof(T)] = handler;
        return this;
    }

    /// <inheritdoc />
    public void SendPacket<T>(T message)
    {
        if (message is not null)
        {
            _sentPackets.Add(message);
        }
    }

    /// <summary>
    /// Simulates receiving a packet from the server, invoking the registered handler.
    /// </summary>
    public void SimulateReceivePacket<T>(T packet)
    {
        if (_messageHandlers.TryGetValue(typeof(T), out var handler))
        {
            ((NetworkServerMessageHandler<T>)handler)(packet);
        }
    }

    /// <summary>
    /// Gets the last packet sent of a specific type.
    /// </summary>
    public T? GetLastSentPacket<T>() where T : class
    {
        return _sentPackets.OfType<T>().LastOrDefault();
    }

    /// <summary>
    /// Clears all sent packets.
    /// </summary>
    public void ClearSentPackets()
    {
        _sentPackets.Clear();
    }

    /// <summary>
    /// Verifies that a packet of the specified type was sent.
    /// </summary>
    public bool WasPacketSent<T>()
    {
        return _sentPackets.OfType<T>().Any();
    }

    /// <summary>
    /// Gets the count of packets sent of a specific type.
    /// </summary>
    public int GetSentPacketCount<T>()
    {
        return _sentPackets.OfType<T>().Count();
    }
}
