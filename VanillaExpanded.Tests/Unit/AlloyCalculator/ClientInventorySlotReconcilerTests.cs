using Moq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies directional restrictions, transfer ordering, and authoritative movement accounting.</summary>
[Trait("Category", "Unit")]
public class ClientInventorySlotReconcilerTests
{
    #region Public API

    #region Slot Corrections
    /// <summary>Verifies that an already-correct target produces no inventory packet.</summary>
    [Fact]
    public void CorrectState_DoesNothing()
    {
        TestContext context = CreateContext(targetItemId: 1, targetAmount: 5);

        InventorySlotCorrectionResult result = Remove(context, desiredItemId: 1, desiredAmount: 5, out int retained);

        Assert.Equal(InventorySlotCorrectionResult.Success, result);
        Assert.Equal(5, retained);
        Assert.Equal(5, context.Target.StackSize);
        context.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    /// <summary>Verifies that only excess matching items are returned to the player.</summary>
    [Fact]
    public void MatchingExcess_RemovesOnlyDifference()
    {
        TestContext context = CreateContext(targetItemId: 1, targetAmount: 8);

        InventorySlotCorrectionResult result = Remove(context, desiredItemId: 1, desiredAmount: 5, out int retained);

        Assert.Equal(InventorySlotCorrectionResult.Success, result);
        Assert.Equal(5, retained);
        Assert.Equal(5, context.Target.StackSize);
        Assert.Equal(3, context.Fixture.BackpackInventory[0].StackSize);
    }

    /// <summary>Verifies that only a matching deficit is added to the target.</summary>
    [Fact]
    public void MatchingDeficit_AddsOnlyDifference()
    {
        TestContext context = CreateContext(targetItemId: 1, targetAmount: 2, sourceItemId: 1, sourceAmount: 10);

        InventorySlotCorrectionResult result = ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi,
            context.Fixture.Player,
            context.Target,
            [.. context.Fixture.BackpackInventory, .. context.Fixture.HotbarInventory],
            stack => stack.Id == 1,
            amount: 3);

        Assert.Equal(InventorySlotCorrectionResult.Success, result);
        Assert.Equal(5, context.Target.StackSize);
        Assert.Equal(7, context.Fixture.BackpackInventory[0].StackSize);
    }

    /// <summary>Verifies that a mismatched target is evacuated before the desired stack is inserted.</summary>
    [Fact]
    public void DifferentItem_ReplacesStack()
    {
        TestContext context = CreateContext(targetItemId: 2, targetAmount: 4, sourceItemId: 1, sourceAmount: 10);

        InventorySlotCorrectionResult removeResult = Remove(context, desiredItemId: 1, desiredAmount: 5, out int retained);
        InventorySlotCorrectionResult addResult = ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi,
            context.Fixture.Player,
            context.Target,
            [.. context.Fixture.BackpackInventory, .. context.Fixture.HotbarInventory],
            stack => stack.Id == 1,
            amount: 5 - retained);

        Assert.Equal(InventorySlotCorrectionResult.Success, removeResult);
        Assert.Equal(InventorySlotCorrectionResult.Success, addResult);
        Assert.Equal(1, context.Target.Itemstack!.Id);
        Assert.Equal(5, context.Target.StackSize);
    }

    #endregion

    #region Priority And Restrictions

    /// <summary>Verifies stable smallest-stack-first withdrawal across the supplied pool.</summary>
    [Fact]
    public void WithdrawalPriority_SmallestFirstAndStableTies()
    {
        TestContext context = CreateContext(1, 1, 1, 3);
        context.Fixture.HotbarInventory[0].Itemstack = context.Target.Itemstack!.Clone();
        context.Fixture.HotbarInventory[0].Itemstack!.StackSize = 2;
        context.Fixture.HotbarInventory[1].Itemstack = context.Target.Itemstack!.Clone();
        context.Fixture.HotbarInventory[1].Itemstack!.StackSize = 2;
        var slots = new[] { context.Fixture.BackpackInventory[0], context.Fixture.HotbarInventory[0], context.Fixture.HotbarInventory[1] };
        Assert.Equal(InventorySlotCorrectionResult.Success, ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi, context.Fixture.Player, context.Target, slots, stack => stack.Id == 1, 3));
        Assert.Equal(3, slots[0].StackSize);
        Assert.True(slots[1].Empty);
        Assert.Equal(1, slots[2].StackSize);
        Assert.Equal(4, context.Target.StackSize);
    }

    /// <summary>Verifies existing stacks receive returns before earlier empty slots.</summary>
    [Fact]
    public void ReturnPriority_ExistingStackBeforeEmptySlot()
    {
        TestContext context = CreateContext(1, 8);
        context.Fixture.HotbarInventory[0].Itemstack = context.Target.Itemstack!.Clone();
        context.Fixture.HotbarInventory[0].Itemstack!.StackSize = 2;
        Assert.Equal(InventorySlotCorrectionResult.Success, Remove(context, 1, 5, out int retained));
        Assert.Equal(5, retained);
        Assert.Equal(5, context.Fixture.HotbarInventory[0].StackSize);
        Assert.True(context.Fixture.BackpackInventory[0].Empty);
    }

    /// <summary>Verifies restricted target slots reject withdrawals without invoking vanilla transfers.</summary>
    [Fact]
    public void RestrictedDestination_DoesNotTransfer()
    {
        TestContext context = CreateContext(1, 1, 1, 5);
        var restricted = new Mock<ItemSlot>(context.Target.Inventory);
        restricted.Setup(d => d.CanHold(It.IsAny<ItemSlot>())).Returns(false);
        Assert.Equal(InventorySlotCorrectionResult.InsufficientItems, ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi, context.Fixture.Player, restricted.Object,
            [context.Fixture.BackpackInventory[0]], stack => stack.Id == 1, 3));
        Assert.Equal(5, context.Fixture.BackpackInventory[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    /// <summary>Verifies specialized source withdrawal restrictions are honored independently of matching.</summary>
    [Fact]
    public void RestrictedSource_DoesNotTransfer()
    {
        TestContext context = CreateContext(1, 1);
        var restricted = new Mock<ItemSlot>(context.Fixture.BackpackInventory);
        restricted.Object.Itemstack = context.Target.Itemstack!.Clone();
        restricted.Object.Itemstack!.StackSize = 5;
        restricted.Setup(d => d.CanTake()).Returns(false);
        Assert.Equal(InventorySlotCorrectionResult.InsufficientItems, ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi, context.Fixture.Player, context.Target,
            [restricted.Object], stack => stack.Id == 1, 3));
        Assert.Equal(5, restricted.Object.StackSize);
        Assert.Equal(1, context.Target.StackSize);
    }

    /// <summary>Verifies inventory containment remains authoritative even when a specialized slot accepts the source.</summary>
    [Fact]
    public void InventoryContainmentRejects_OverridesSpecializedSlotAcceptance()
    {
        TestContext context = CreateContext(1, 1, 1, 5);
        var inventory = new Mock<InventoryGeneric>(1, "restricted", "1", null!, null!);
        inventory.Setup(v => v.CanContain(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>())).Returns(false);
        var target = new Mock<ItemSlot>(inventory.Object);
        target.Setup(v => v.CanHold(It.IsAny<ItemSlot>())).Returns(true);
        target.Setup(v => v.CanTakeFrom(It.IsAny<ItemSlot>(), EnumMergePriority.AutoMerge)).Returns(true);
        Assert.Equal(InventorySlotCorrectionResult.InsufficientItems, ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi, context.Fixture.Player, target.Object,
            [context.Fixture.BackpackInventory[0]], stack => stack.Id == 1, 3));
        Assert.Equal(5, context.Fixture.BackpackInventory[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    /// <summary>Verifies a source removed during a permission callback cannot receive a stale transfer.</summary>
    [Fact]
    public void PermissionCallbackClosesSource_RechecksImmediatelyBeforeTransfer()
    {
        TestContext context = CreateContext(1, 1, 1, 5);
        bool eligible = true;
        var target = new Mock<ItemSlot>(context.Target.Inventory);
        target.Setup(v => v.CanTakeFrom(It.IsAny<ItemSlot>(), EnumMergePriority.AutoMerge)).Returns(true);
        target.Setup(v => v.CanHold(It.IsAny<ItemSlot>())).Returns(() => { eligible = false; return true; });
        Assert.Equal(InventorySlotCorrectionResult.InsufficientItems, ClientInventorySlotReconciler.AddMissing(
            context.Fixture.ClientApi, context.Fixture.Player, target.Object,
            [context.Fixture.BackpackInventory[0]], stack => stack.Id == 1, 3,
            isExternalSlotEligible: _ => eligible));
        Assert.Equal(5, context.Fixture.BackpackInventory[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    /// <summary>Verifies mid-return closure preserves the first partial movement and reports remaining space failure.</summary>
    [Fact]
    public void ReturnDestinationClosesAfterPacket_PreservesPartialMovement()
    {
        TestContext context = CreateContext(1, 8);
        ItemSlot first = context.Fixture.BackpackInventory[0];
        ItemSlot second = context.Fixture.BackpackInventory[1];
        first.Itemstack = context.Target.Itemstack!.Clone();
        first.Itemstack!.StackSize = 63;
        bool eligible = true;
        context.Fixture.ClientNetworkMock!.Setup(n => n.SendPacketClient(It.IsAny<object>())).Callback(() => eligible = false);
        Assert.Equal(InventorySlotCorrectionResult.InsufficientSpace, ClientInventorySlotReconciler.RemoveIncorrectOrExcess(
            context.Fixture.ClientApi, context.Fixture.Player, context.Target, [first, second],
            stack => stack.Id == 1, 5, out _, isExternalSlotEligible: _ => eligible));
        Assert.Equal(64, first.StackSize);
        Assert.True(second.Empty);
        Assert.Equal(7, context.Target.StackSize);
        context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Once);
    }

    #endregion

    #endregion

    #region Private

    /// <summary>Runs the removal half of reconciliation for a test context.</summary>
    private static InventorySlotCorrectionResult Remove(
        TestContext context,
        int desiredItemId,
        int desiredAmount,
        out int retained)
    {
        return ClientInventorySlotReconciler.RemoveIncorrectOrExcess(
            context.Fixture.ClientApi,
            context.Fixture.Player,
            context.Target,
            [.. context.Fixture.BackpackInventory, .. context.Fixture.HotbarInventory],
            stack => stack.Id == desiredItemId,
            desiredAmount,
            out retained);
    }

    /// <summary>Creates inventories with an isolated target slot and optional player source stack.</summary>
    private static TestContext CreateContext(
        int targetItemId,
        int targetAmount,
        int? sourceItemId = null,
        int sourceAmount = 0)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var targetInventory = new InventoryGeneric(1, "target", "target-1", null!);
        targetInventory.Api = fixture.Api;
        targetInventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        MockItem targetItem = fixture.CreateNonLightSource(targetItemId);
        targetItem.Code = new AssetLocation("game", $"item-{targetItemId}");
        targetItem.MaxStackSize = 64;
        targetInventory[0].Itemstack = new ItemStack(targetItem, targetAmount);

        if (sourceItemId is int id)
        {
            MockItem sourceItem = fixture.CreateNonLightSource(id);
            sourceItem.Code = new AssetLocation("game", $"item-{id}");
            sourceItem.MaxStackSize = 64;
            fixture.BackpackInventory[0].Itemstack = new ItemStack(sourceItem, sourceAmount);
        }

        return new TestContext(fixture, targetInventory[0]);
    }

    /// <summary>Owns the inventory and target state used by the reconciler tests.</summary>
    private sealed record TestContext(VsTestFixture Fixture, ItemSlot Target);

    #endregion
}