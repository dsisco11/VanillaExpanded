using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Supplies real container inventories while observing block synchronization through Moq.</summary>
internal sealed class ContainerLifecycleCase
{
    public TransferCase Transfer { get; }
    private readonly Mock<BlockEntityContainer>? generic;
    private readonly Mock<BlockEntityCrate>? crate;

    #region Public API
    /// <summary>Initializes the selected engine inventory and replaces only the block synchronization boundary.</summary>
    public ContainerLifecycleCase(bool useCrate)
    {
        Transfer = useCrate
            ? new TransferCase(createTarget: fixture => (InventoryGeneric)new InitializedCrate(fixture.Api).Inventory)
            : new TransferCase();
        if (useCrate)
        {
            crate = new Mock<BlockEntityCrate>();
            crate.SetupGet(block => block.Inventory).Returns(Transfer.Target);
            crate.SetupGet(block => block.InventoryClassName).Returns("crate");
            crate.Object.Pos = new BlockPos(0);
        }
        else
        {
            generic = new Mock<BlockEntityContainer> { CallBase = true };
            generic.SetupGet(block => block.Inventory).Returns(Transfer.Target);
            generic.SetupGet(block => block.InventoryClassName).Returns("lifecycle");
            generic.Setup(block => block.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
            generic.Object.Pos = new BlockPos(0);
        }
        Seed(Transfer.Target[0], 10);
        // Occupy the other destination so limited-capacity tests cannot spill into an empty slot.
        var unrelated = new MockItem(2, api: Transfer.Fixture.Api) { Code = new AssetLocation("game:lifecycle-unrelated") };
        Transfer.Target[1].Itemstack = new ItemStack(unrelated, 1);
        var otherSource = new MockItem(3, api: Transfer.Fixture.Api) { Code = new AssetLocation("game:lifecycle-other-source") };
        Transfer.Fixture.HotbarInventory[9].Itemstack = new ItemStack(otherSource, 3);
    }

    /// <summary>Seeds compatible stacks with a relevant attribute that must survive movement.</summary>
    public void Seed(ItemSlot slot, int quantity)
    {
        slot.Itemstack = new ItemStack(Transfer.Item, quantity);
        slot.Itemstack.Attributes.SetString("fixture", "preserved");
    }

    /// <summary>Runs the selected complete container operation, including its dirty-notification decision.</summary>
    public bool Run()
    {
        var fixture = Transfer.Fixture;
        return crate is not null
            ? BlockBehaviorAutoStashable.AutoStashToCrate(fixture.World, fixture.Player, crate.Object, "test")
            : BlockBehaviorAutoStashable.AutoStashToGenericContainer(fixture.World, fixture.Player, generic!.Object, "test");
    }

    /// <summary>Checks exact synchronization count and arguments without requiring a loaded block world.</summary>
    public void AssertDirty(int count)
    {
        if (crate is not null)
        {
            crate.Verify(block => block.MarkDirty(false, null!), Times.Exactly(count));
            crate.Verify(block => block.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Exactly(count));
        }
        else
        {
            generic!.Verify(block => block.MarkDirty(false, null!), Times.Exactly(count));
            generic.Verify(block => block.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Exactly(count));
        }
    }
    #endregion
}
