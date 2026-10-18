using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Provides an installed server connection inventory and captured owner-thread/control callbacks without world generation.</summary>
internal sealed class ServerDeliveryFixture
{
    internal ServerMain World { get; } = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
    internal Mock<ICoreServerAPI> Api { get; } = new();
    internal Mock<IServerEventAPI> Events { get; } = new();
    internal Mock<IWorldManagerAPI> Maps { get; } = new(MockBehavior.Strict);
    internal Action<float> Tick { get; private set; } = null!;
    internal Action Shutdown { get; private set; } = null!;
    internal NetworkClientMessageHandler<EcosystemCacheReset> Reset { get; private set; } = null!;

    #region Public API
    /// <summary>Captures the same authenticated reset delegate and tick/shutdown listeners installed by the actual source.</summary>
    internal ServerDeliveryFixture()
    {
        World.Clients = new();
        Api.SetupGet(value => value.World).Returns(World);
        Api.SetupGet(value => value.Side).Returns(EnumAppSide.Server);
        Api.SetupGet(value => value.Event).Returns(Events.Object);
        Api.SetupGet(value => value.WorldManager).Returns(Maps.Object);
        Api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        Events.Setup(value => value.RegisterGameTickListener(It.IsAny<Action<float>>(), It.IsAny<int>(), 0))
            .Callback<Action<float>, int, int>((callback, _, _) => Tick = callback).Returns(7);
        Events.Setup(value => value.ServerRunPhase(EnumServerRunPhase.Shutdown, It.IsAny<Action>()))
            .Callback<EnumServerRunPhase, Action>((_, callback) => Shutdown = callback);
        var network = new Mock<IServerNetworkAPI>();
        var channel = new Mock<IServerNetworkChannel>();
        Api.SetupGet(value => value.Network).Returns(network.Object);
        network.Setup(value => value.RegisterChannel(EcosystemCacheReset.ChannelName)).Returns(channel.Object);
        channel.Setup(value => value.RegisterMessageType<EcosystemCacheReset>()).Returns(channel.Object);
        channel.Setup(value => value.SetMessageHandler(It.IsAny<NetworkClientMessageHandler<EcosystemCacheReset>>()))
            .Callback<NetworkClientMessageHandler<EcosystemCacheReset>>(callback => Reset = callback).Returns(channel.Object);
    }
    #endregion
}
