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
    public void FullInventory_DoesNotPreventTryingLaterLootAndDoesNotLoop()
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
    public void InvalidationDuringPrediction_SendsAlreadyPredictedPacketsButStopsNextSlot()
    {
        using var test = new AutoLootTestCase();
        test.Inventory[1].Itemstack = Assert.IsType<ItemStack>(test.Inventory[0].Itemstack).Clone();
        bool valid = true;
        object packet = new();
        test.TransferOverride = (_, _) =>
        {
            valid = false;
            test.Dialog.TryClose();
            return [packet];
        };

        AutoLootTransferCoordinator.Collect(test.Fixture.ClientApi, test.Player.Object, test.ResolveTarget(), () => valid);

        Assert.Single(test.Attempts);
        Assert.Same(packet, Assert.Single(test.Sent).Packet);
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
