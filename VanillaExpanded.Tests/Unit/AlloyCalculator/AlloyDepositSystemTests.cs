using Moq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.ModSystems;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class AlloyDepositSystemTests
{
    [Fact]
    public void RequestDeposit_ChannelAvailable_SendsRequest()
    {
        // Arrange
        var fixture = VsTestFixture.Client();
        var channel = new MockClientNetworkChannel();
        fixture.ClientNetworkMock!
            .Setup(network => network.GetChannel(Constants.ModId))
            .Returns(channel);
        var system = new AlloyDepositSystem();
        system.StartClientSide(fixture.ClientApi);
        Packet_RequestAlloyDeposit request = CreateRequest();

        // Act
        bool sent = system.RequestDeposit(request);

        // Assert
        Assert.True(sent);
        Assert.Same(request, Assert.Single(channel.SentPackets));
    }

    [Fact]
    public void DepositResult_Received_RaisesCompletionEvent()
    {
        // Arrange
        var fixture = VsTestFixture.Client();
        var channel = new MockClientNetworkChannel();
        fixture.ClientNetworkMock!
            .Setup(network => network.GetChannel(Constants.ModId))
            .Returns(channel);
        var system = new AlloyDepositSystem();
        system.StartClientSide(fixture.ClientApi);
        Packet_AlloyDepositResult? received = null;
        system.DepositCompleted += result => received = result;
        var response = new Packet_AlloyDepositResult
        {
            RequestId = "request-1",
            ResultCode = AlloyDepositResultCode.Success
        };

        // Act
        channel.SimulateReceivePacket(response);

        // Assert
        Assert.Same(response, received);
    }

    private static Packet_RequestAlloyDeposit CreateRequest()
    {
        return new Packet_RequestAlloyDeposit
        {
            RequestId = "request-1",
            Position = new BlockPos(0),
            AlloyCode = "game:ingot-bronze",
            SlotIndices = [0, 1, 2, 3],
            SlotIngredientCodes =
            [
                "game:ingot-copper",
                "game:ingot-copper",
                "game:ingot-copper",
                "game:ingot-tin"
            ],
            SlotAmounts = [3, 3, 3, 1]
        };
    }
}
