using Moq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class ClientInventorySlotReconcilerTests
{
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

    private sealed record TestContext(VsTestFixture Fixture, ItemSlot Target);
}