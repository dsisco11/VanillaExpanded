using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Observes merge feedback addressed to the exact inventory owner instead of other online players.</summary>
internal sealed class TransferErrorObservation
{
    private readonly Mock<IServerPlayer> owner;
    private readonly Mock<IServerPlayer> other;

    #region Public API
    /// <summary>Installs another player before the inventory owner to distinguish identity from enumeration order.</summary>
    public TransferErrorObservation(TransferCase test)
    {
        // Class-backed mocks inherit the engine's inaccessible interface implementation while remapping chat and inventory members.
        owner = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IServerPlayer>();
        other = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IServerPlayer>();
        owner.Setup(player => player.InventoryManager).Returns(test.Fixture.Player);
        other.Setup(player => player.InventoryManager).Returns(new Mock<IPlayerInventoryManager>().Object);
        test.Fixture.WorldMock.Setup(world => world.AllOnlinePlayers).Returns(new IPlayer[] { other.Object, owner.Object });
    }

    /// <summary>Checks exact error-chat count, group, type, meaningful text, and absence of delivery to another player.</summary>
    public void AssertErrors(int count)
    {
        owner.Verify(player => player.SendMessage(GlobalConstants.GeneralChatGroup,
            It.Is<string>(message => !string.IsNullOrWhiteSpace(message)), EnumChatType.CommandError, null!), Times.Exactly(count));
        owner.Verify(player => player.SendMessage(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<EnumChatType>(), It.IsAny<string>()), Times.Exactly(count));
        other.Verify(player => player.SendMessage(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<EnumChatType>(), It.IsAny<string>()), Times.Never);
    }
    #endregion
}
