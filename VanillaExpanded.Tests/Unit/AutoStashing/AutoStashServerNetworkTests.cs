using Moq;

using VanillaExpanded.Network;
using VanillaExpanded.src.ModSystems;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

[Trait("Category", "Unit")]
public class AutoStashServerNetworkTests
{
    [Fact]
    public void StartServerSide_RegistersRequestHandler()
    {
        // Arrange
        var fixture = VsTestFixture.Server();
        var channel = new MockServerNetworkChannel();
        var network = new Mock<IServerNetworkAPI>();
        network.Setup(api => api.GetChannel(Constants.ModId)).Returns(channel);
        fixture.ServerApiMock!.Setup(api => api.Network).Returns(network.Object);

        var system = new AutoStashSystem_Server();

        // Act
        system.StartServerSide(fixture.ServerApi);

        // Assert
        Assert.True(channel.HasMessageHandler<Packet_RequestAutoStash>());
        Assert.True(channel.HasMessageHandler<Packet_RequestEntityAutoStash>());
    }

    [Fact]
    public void RequestAutoStash_PositionMissing_IsSafelyIgnored()
    {
        // Arrange
        var fixture = VsTestFixture.Server();
        var channel = new MockServerNetworkChannel();
        var network = new Mock<IServerNetworkAPI>();
        network.Setup(api => api.GetChannel(Constants.ModId)).Returns(channel);
        fixture.ServerApiMock!.Setup(api => api.Network).Returns(network.Object);

        var system = new AutoStashSystem_Server();
        system.StartServerSide(fixture.ServerApi);

        // Act
        channel.SimulateReceivePacket(null!, new Packet_RequestAutoStash
        {
            position = null!
        });

        // Assert
        fixture.LoggerMock.Verify(
            logger => logger.Warning("[AutoStash] Ignoring request with no target position."),
            Times.Once);
    }

}