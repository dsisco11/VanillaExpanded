using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.AutoStashing.Targets;
using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Provides real inventories and records lifecycle effects for shared-transfer regression scenarios.</summary>
internal sealed class TransferCase
{
    public VsTestFixture Fixture { get; } = VsTestFixture.Server();
    public InventoryGeneric Target { get; }
    public MockItem Item { get; }
    public List<int> ModifiedSlots { get; } = [];

    #region Public API
    /// <summary>Creates a target with engine slots and a stable matching collectible identity.</summary>
    public TransferCase(int slots = 2, System.Func<VsTestFixture, InventoryGeneric>? createTarget = null)
    {
        Target = createTarget?.Invoke(Fixture) ?? new InventoryGeneric(slots, "edge", "test", null!);
        Target.Api = Fixture.Api;
        Target.InvNetworkUtil = Fixture.InvNetworkUtilMock.Object;
        Target.SlotModified += index => ModifiedSlots.Add(index);
        Item = new MockItem(1, api: Fixture.Api) { Code = new AssetLocation("game:transfer-edge"), MaxStackSize = 64 };
    }

    /// <summary>Installs an observed source slot into a real source inventory.</summary>
    public ObservedTransferSlot Source(int quantity, int index = 0, bool hotbar = false, CollectibleObject? item = null)
    {
        InventoryGeneric inventory = hotbar ? Fixture.HotbarInventory : Fixture.BackpackInventory;
        var slot = new ObservedTransferSlot(inventory) { Itemstack = new ItemStack(item ?? Item, quantity) };
        inventory[index] = slot;
        return slot;
    }

    /// <summary>Runs the actual service against current inventories with optional preferred-slot characterization.</summary>
    public int Run(System.Func<ItemStack, int?>? preferred = null)
    {
        return AutoStashService.Execute(Fixture.World, Fixture.Player, "test", new InventoryAutoStashTarget(Target),
            new BlockPos(0), "transfer-edge", new MatchingContentsPolicy(_ => true, preferred)).MovedQuantity;
    }

    /// <summary>Verifies exact open/close ownership and that shared execution never uses client transfer packets.</summary>
    public void AssertSessions(int expected)
    {
        Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(Target), Times.Exactly(expected));
        Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(Target), Times.Exactly(expected));
        Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
    }
    #endregion
}
