using Moq;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootTransferTests
{
    [Fact]
    public void Collect_SendsEveryReturnedPacketOnceWithEntityAndZeroOffset()
    {
        using var test = new AutoLootTestCase();
        object[] packets = [new object(), new object()];
        test.TransferOverride = (_, _) => packets;

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Single(test.Attempts);
        Assert.Equal(packets, test.Sent.Select(entry => entry.Packet));
        Assert.All(test.Sent, entry => { Assert.Equal(42, entry.EntityId); Assert.Equal(0, entry.Offset); });
        test.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void RejectedMoves_DoNotPreventTryingLaterLootAndDoNotLoop()
    {
        using var test = new AutoLootTestCase();
        test.Inventory[1].Itemstack = new ItemStack(test.Fixture.CreateNonLightSource(2), 2);
        test.Inventory[2].Itemstack = new ItemStack(test.Fixture.CreateNonLightSource(3), 1);
        test.TransferOverride = (source, operation) => source == test.Inventory[1]
            ? test.TransferToPlayer(source, operation) : null;

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(3, test.Attempts.Count);
        Assert.Equal(3, test.Inventory[0].StackSize);
        Assert.True(test.Inventory[1].Empty);
        Assert.Equal(1, test.Inventory[2].StackSize);
        Assert.Single(test.Sent);
    }

    [Fact]
    public void PartialCapacity_LeavesOverflowAndPreservesAttributes()
    {
        using var test = new AutoLootTestCase();
        test.UseVanillaTransfers();
        ItemStack stack = Assert.IsType<ItemStack>(test.Inventory[0].Itemstack);
        stack.Attributes.SetString("marker", "keep");
        foreach (ItemSlot slot in test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory))
        {
            slot.Itemstack = stack.Clone();
            slot.Itemstack.StackSize = 64;
        }
        Assert.IsType<ItemStack>(test.Fixture.BackpackInventory[0].Itemstack).StackSize = 63;
        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(2, test.Inventory[0].StackSize);
        Assert.Equal("keep", Assert.IsType<ItemStack>(test.Inventory[0].Itemstack).Attributes.GetString("marker"));
        Assert.Equal(64, test.Fixture.BackpackInventory[0].StackSize);
        Assert.Equal("keep", Assert.IsType<ItemStack>(test.Fixture.BackpackInventory[0].Itemstack).Attributes.GetString("marker"));
        Assert.Single(test.Sent);
    }

    [Fact]
    public void SufficientCapacity_CollectsMultipleTypesAndPreservesAttributesUsingVanillaPrediction()
    {
        using var test = new AutoLootTestCase();
        test.UseVanillaTransfers();
        test.Inventory[1].Itemstack = new ItemStack(test.Fixture.CreateNonLightSource(2), 2);
        test.Inventory[2].Itemstack = new ItemStack(test.Fixture.CreateNonLightSource(3), 1);
        for (int index = 0; index < test.Inventory.Count; index++)
        {
            test.Inventory[index].Itemstack!.Attributes.SetString("marker", $"loot-{index}");
        }

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.All(test.Inventory, slot => Assert.True(slot.Empty));
        ItemStack[] moved = test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory)
            .Where(slot => !slot.Empty).Select(slot => slot.Itemstack!).OrderBy(stack => stack.Id).ToArray();
        Assert.Equal(new[] { 3, 2, 1 }, moved.Select(stack => stack.StackSize));
        Assert.Equal(new[] { "loot-0", "loot-1", "loot-2" },
            moved.Select(stack => stack.Attributes.GetString("marker")));
        Assert.Equal(3, test.Attempts.Count);
        Assert.Equal(3, test.Sent.Count);
        Assert.All(test.Sent, entry => Assert.NotNull(Assert.IsType<Packet_Client>(entry.Packet).MoveItemstack));
        test.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);
        Assert.DoesNotContain(test.Fixture.InvNetworkUtilMock.Invocations,
            invocation => invocation.Method.Name.Contains("Activate"));
    }

    [Fact]
    public void FullPlayerInventories_LeaveAllLootAndIgnoreAnUnrelatedOpenContainer()
    {
        using var test = new AutoLootTestCase();
        var unrelated = new InventoryGeneric(3, "chest", "unrelated", null!)
        {
            Api = test.Fixture.Api, InvNetworkUtil = test.Fixture.InvNetworkUtilMock.Object
        };
        var manager = test.UseVanillaTransfers(unrelated);
        test.Inventory[1].Itemstack = new ItemStack(test.Fixture.CreateNonLightSource(2), 2);
        test.Inventory[2].Itemstack = new ItemStack(test.Fixture.CreateNonLightSource(3), 1);
        ItemStack stack = Assert.IsType<ItemStack>(test.Inventory[0].Itemstack);
        foreach (ItemSlot slot in test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory))
        {
            slot.Itemstack = stack.Clone();
            slot.Itemstack.StackSize = 64;
        }

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(new[] { 3, 2, 1 }, test.Inventory.Select(slot => slot.StackSize));
        Assert.All(unrelated, slot => Assert.True(slot.Empty));
        Assert.All(test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory),
            slot => Assert.Equal(64, slot.StackSize));
        Assert.Equal(3, test.Attempts.Count);
        Assert.Empty(test.Sent);

        // Prove this open container would be a valid destination without the player-only flag.
        var operation = new ItemStackMoveOperation(test.Fixture.World, EnumMouseButton.Left,
            EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, stack.StackSize) { ActingPlayer = test.Player.Object };
        Assert.NotEmpty(manager.TryTransferAway(test.Inventory[0], ref operation, false, false));
        Assert.Contains(unrelated, slot => !slot.Empty);
    }

    [Fact]
    public void RestrictedSlots_AreSkippedAndSlotCapacityLeavesOverflow()
    {
        using var test = new AutoLootTestCase();
        test.UseVanillaTransfers();
        foreach (ItemSlot slot in test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory))
        {
            slot.StorageType = 0;
        }
        ItemSlot allowed = test.Fixture.BackpackInventory[1];
        allowed.StorageType = EnumItemStorageFlags.General;
        allowed.MaxSlotStackSize = 1;

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(2, test.Inventory[0].StackSize);
        Assert.Equal(1, allowed.StackSize);
        Assert.All(test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory)
            .Where(slot => !ReferenceEquals(slot, allowed)), slot => Assert.True(slot.Empty));
        Assert.Single(test.Attempts);
        Assert.Single(test.Sent);
    }

    [Fact]
    public void DifferentAttributes_DoNotMergeEvenWhenTheItemTypeHasCapacity()
    {
        using var test = new AutoLootTestCase();
        test.UseVanillaTransfers();
        ItemStack source = Assert.IsType<ItemStack>(test.Inventory[0].Itemstack);
        source.Attributes.SetString("marker", "corpse");
        foreach (ItemSlot slot in test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory))
        {
            slot.Itemstack = source.Clone();
            slot.Itemstack.Attributes.SetString("marker", "player");
            slot.Itemstack.StackSize = 1;
        }

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(3, source.StackSize);
        Assert.Equal("corpse", source.Attributes.GetString("marker"));
        Assert.All(test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory), slot =>
        {
            Assert.Equal(1, slot.StackSize);
            Assert.Equal("player", slot.Itemstack!.Attributes.GetString("marker"));
        });
        Assert.Empty(test.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoReturnedPackets_DoesNotSendOrRetryAndContinuesToLaterSlots(bool emptyArray)
    {
        using var test = new AutoLootTestCase();
        test.Inventory[2].Itemstack = test.Inventory[0].Itemstack!.Clone();
        test.TransferOverride = (_, _) => emptyArray ? [] : null;

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(new[] { test.Inventory[0], test.Inventory[2] }, test.Attempts);
        Assert.Empty(test.Sent);
        Assert.Equal(3, test.Inventory[0].StackSize);
        Assert.Equal(3, test.Inventory[2].StackSize);
    }

    [Fact]
    public void AddedSourceSlots_AreNotVisitedDuringTheCurrentBoundedPass()
    {
        using var test = new AutoLootTestCase();
        test.Inventory[2].Itemstack = test.Inventory[0].Itemstack!.Clone();
        test.TransferOverride = (_, _) =>
        {
            test.Inventory.AddSlots(1);
            test.Inventory[test.Inventory.Count - 1].Itemstack = test.Inventory[0].Itemstack!.Clone();
            return [new object()];
        };

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Equal(new[] { test.Inventory[0], test.Inventory[2] }, test.Attempts);
        Assert.Equal(5, test.Inventory.Count);
        Assert.Equal(2, test.Sent.Count);
    }

    [Fact]
    public void InvalidationDuringPrediction_SendsAlreadyPredictedPacketsButStopsNextSlot()
    {
        using var test = new AutoLootTestCase();
        test.Inventory[1].Itemstack = Assert.IsType<ItemStack>(test.Inventory[0].Itemstack).Clone();
        bool valid = true;
        object[] packets = [new object(), new object()];
        test.TransferOverride = (_, _) =>
        {
            valid = false;
            test.Dialog.TryClose();
            return packets;
        };

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => valid);

        Assert.Single(test.Attempts);
        Assert.Equal(packets, test.Sent.Select(entry => entry.Packet));
    }

    [Fact]
    public void TargetInvalidationDuringPrediction_StopsNextSlotEvenWhenContinuationStillAllowsIt()
    {
        using var test = new AutoLootTestCase();
        test.Inventory[1].Itemstack = test.Inventory[0].Itemstack!.Clone();
        object[] packets = [new object(), new object()];
        test.TransferOverride = (_, _) =>
        {
            test.WorldEntity = null;
            return packets;
        };

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => true);

        Assert.Single(test.Attempts);
        Assert.Equal(packets, test.Sent.Select(entry => entry.Packet));
    }

    [Fact]
    public void AlreadyInvalidated_DoesNotPredictOrSend()
    {
        using var test = new AutoLootTestCase();
        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => false);
        Assert.Empty(test.Attempts);
        Assert.Empty(test.Sent);
    }
}
