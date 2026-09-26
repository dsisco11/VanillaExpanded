using Moq;

using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

[Trait("Category", "Unit")]
public class AutoStashClientNetworkTests
{
    [Fact]
    public void RequestAutoStash_ChannelAvailable_SendsCopiedPosition()
    {
        // Arrange
        var fixture = VsTestFixture.Client();
        var channel = new MockClientNetworkChannel();
        fixture.ClientNetworkMock!
            .Setup(network => network.GetChannel(Constants.ModId))
            .Returns(channel);

        var system = new AutoStashSystem_Client();
        system.StartClientSide(fixture.ClientApi);
        var position = new BlockPos(10, 20, 30);

        // Act
        system.RequestAutoStash(position);
        position.X = 99;

        // Assert
        Packet_RequestAutoStash packet = Assert.IsType<Packet_RequestAutoStash>(Assert.Single(channel.SentPackets));
        Assert.Equal(10, packet.position.X);
        Assert.Equal(20, packet.position.Y);
        Assert.Equal(30, packet.position.Z);
    }

    [Fact]
    public void RequestAutoStash_ChannelUnavailable_LogsErrorAndDoesNotThrow()
    {
        // Arrange
        var fixture = VsTestFixture.Client();
        fixture.ClientNetworkMock!
            .Setup(network => network.GetChannel(Constants.ModId))
            .Returns((Vintagestory.API.Client.IClientNetworkChannel)null!);

        var system = new AutoStashSystem_Client();
        system.StartClientSide(fixture.ClientApi);

        // Act
        system.RequestAutoStash(new BlockPos(0));

        // Assert
        fixture.LoggerMock.Verify(
            logger => logger.Error("Cannot send auto-stash request packet: Network channel is null."),
            Times.Once);
    }

    [Fact]
    public void RequestEntityAutoStash_ChannelAvailable_SendsEntityAndAttachmentSlot()
    {
        var fixture = VsTestFixture.Client();
        var channel = new MockClientNetworkChannel();
        fixture.ClientNetworkMock!
            .Setup(network => network.GetChannel(Constants.ModId))
            .Returns(channel);

        var system = new AutoStashSystem_Client();
        system.StartClientSide(fixture.ClientApi);

        system.RequestEntityAutoStash(42, 3);

        Packet_RequestEntityAutoStash packet = Assert.IsType<Packet_RequestEntityAutoStash>(Assert.Single(channel.SentPackets));
        Assert.Equal(42, packet.EntityId);
        Assert.Equal(3, packet.AttachmentSlotIndex);
    }
}