using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Provides engine-backed bloomery inputs and precise contents/lifecycle assertions.</summary>
internal sealed class BloomeryCase
{
    public VsTestFixture Fixture { get; } = VsTestFixture.Server();
    public Mock<TestableBlockEntityBloomery> TargetMock { get; }
    public TestableBlockEntityBloomery Target { get; }
    public MockItem Ore { get; }
    public MockItem Fuel { get; }
    public MockItem Invalid { get; }
    public List<int> ModifiedSlots { get; } = [];

    #region Public API
    #region Setup
    /// <summary>Creates stable ore/fuel identities and records actual destination slot notifications.</summary>
    public BloomeryCase(int ratio = 2)
    {
        TargetMock = new Mock<TestableBlockEntityBloomery>(new BlockPos(0), Fixture.Api) { CallBase = true };
        // Keep real acceptance and inventory behavior, intercepting only synchronization.
        TargetMock.Setup(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
        Target = TargetMock.Object;
        Target.TestInventory.SlotModified += index => ModifiedSlots.Add(index);
        Ore = MockItem.CreateBloomeryOre(1, ratio, Fixture.Api);
        Fuel = MockItem.CreateBloomeryFuel(2, Fixture.Api);
        Invalid = MockItem.CreateNonCombustible(3, Fixture.Api);
        Ore.Code = new AssetLocation("game:bloomery-test-ore");
        Fuel.Code = new AssetLocation("game:bloomery-test-fuel");
        Invalid.Code = new AssetLocation("game:bloomery-test-unrelated");
    }

    /// <summary>Seeds a stack with an attribute whose preservation is checked after movement.</summary>
    public void Seed(ItemSlot slot, CollectibleObject item, int quantity)
    {
        slot.Itemstack = new ItemStack(item, quantity);
        slot.Itemstack.Attributes.SetString("fixture", "preserved");
    }

    /// <summary>Installs an observed engine source at the requested source inventory/index.</summary>
    public ObservedTransferSlot Source(CollectibleObject item, int quantity, int index = 0, bool hotbar = false)
    {
        InventoryGeneric inventory = hotbar ? Fixture.HotbarInventory : Fixture.BackpackInventory;
        var slot = new ObservedTransferSlot(inventory);
        inventory[index] = slot;
        Seed(slot, item, quantity);
        return slot;
    }
    #endregion
    #region Execution and assertions
    /// <summary>Captures every source and destination, including output and unrelated player slots.</summary>
    public InventorySnapshot Snapshot() => new(Fixture.BackpackInventory, Fixture.HotbarInventory, Target.TestInventory);

    /// <summary>Invokes the existing complete server bloomery operation.</summary>
    public bool Run() => BlockBehaviorAutoStashable.AutoStashToBloomery(Fixture.World, Fixture.Player, Target, "test");

    /// <summary>Checks exact remainder/destination identity and preservation of the seeded stack attribute.</summary>
    public void AssertSlot(ItemSlot slot, CollectibleObject item, int quantity)
    {
        if (quantity == 0) Assert.True(slot.Empty);
        else
        {
            InventorySnapshot.AssertStack(slot, item, quantity);
            Assert.Equal("preserved", slot.Itemstack!.Attributes.GetString("fixture"));
        }
    }

    /// <summary>Checks conservation, untouched slots, redraw synchronization and absence of inventory sessions/client packets.</summary>
    public void Verify(InventorySnapshot before, bool moved, params ItemSlot[] changed)
    {
        before.AssertUnchangedExcept(changed);
        before.AssertConserved();
        Assert.Equal(moved, ModifiedSlots.Count > 0);
        if (moved)
        {
            TargetMock.Verify(target => target.MarkDirty(true, null!), Times.Once);
            Assert.All(ModifiedSlots, index => Assert.InRange(index, 0, 1));
        }
        TargetMock.Verify(target => target.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()),
            moved ? Times.Once() : Times.Never());
        Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
        Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
    }
    #endregion
    #endregion
}
