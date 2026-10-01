using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Observes merge feedback addressed to the exact inventory owner instead of other online players.</summary>
internal sealed class TransferErrorObservation
{
    private readonly ObservedServerPlayer owner;
    private readonly ObservedServerPlayer other;

    #region Public API
    /// <summary>Installs another player before the inventory owner to distinguish identity from enumeration order.</summary>
    public TransferErrorObservation(TransferCase test)
    {
        owner = ObservedServerPlayer.Create(test.Fixture.Player);
        other = ObservedServerPlayer.Create(new Mock<IPlayerInventoryManager>().Object);
        test.Fixture.WorldMock.Setup(world => world.AllOnlinePlayers).Returns(new IPlayer[] { other, owner });
    }

    /// <summary>Checks exact error-chat count, group, type, meaningful text, and absence of delivery to another player.</summary>
    public void AssertErrors(int count)
    {
        Assert.Equal(count, owner.Messages.Count);
        Assert.All(owner.Messages, error =>
        {
            Assert.Equal(GlobalConstants.GeneralChatGroup, error.Group);
            Assert.Equal(EnumChatType.CommandError, error.Type);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        });
        Assert.Empty(other.Messages);
    }
    #endregion
}
