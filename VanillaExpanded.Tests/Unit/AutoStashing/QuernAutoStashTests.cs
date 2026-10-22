using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.AutoStashing.Targets;
using VanillaExpanded.src.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class QuernAutoStashTests
{
    [Theory]
    [InlineData(1, false, 6)]
    [InlineData(62, false, 2)]
    [InlineData(64, false, 0)]
    [InlineData(62, true, 2)]
    public void Execution_UsesOnlyInputAndBackpackBeforeHotbar(int initial, bool outputOccupied, int moved)
    {
        var test = CreateTransfer();
        test.Target[0].Itemstack = new ItemStack(test.Item, initial);
        if (outputOccupied) test.Target[1].Itemstack = new ItemStack(test.Item, 1);
        var backpack = test.Source(3);
        var hotbar = test.Source(3, hotbar: true);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = Execute(test);

        Assert.Equal(moved, result.MovedQuantity);
        Assert.Equal(initial + moved, test.Target[0].StackSize);
        Assert.Equal(3 - Math.Min(3, moved), backpack.StackSize);
        Assert.Equal(3 - Math.Max(0, moved - 3), hotbar.StackSize);
        Assert.Equal(outputOccupied ? 1 : 0, test.Target[1].StackSize);
        before.AssertUnchangedExcept(moved > 0 ? [backpack, hotbar, test.Target[0]] : []);
        before.AssertConserved();
        test.AssertSessions(moved > 0 ? 1 : 0);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("ungrindable")]
    [InlineData("different")]
    [InlineData("outputonly")]
    [InlineData("attributes")]
    [InlineData("putlocked")]
    [InlineData("takelocked")]
    [InlineData("rejected")]
    public void IneligibleOrRejectedTransfers_DoNotUseOutput(string reason)
    {
        var test = CreateTransfer();
        test.Target[0].Itemstack = reason == "empty" ? null : new ItemStack(test.Item, 1);
        test.Target[1].Itemstack = new ItemStack(test.Item, 1);
        var source = test.Source(3);
        if (reason == "ungrindable") test.Item.GrindingProps = null;
        if (reason is "different" or "outputonly")
        {
            var other = new MockItem(2, api: test.Fixture.Api)
                { Code = new AssetLocation("game:different"), GrindingProps = new GrindingProperties() };
            source.Itemstack = new ItemStack(other, 3);
            if (reason == "outputonly") test.Target[1].Itemstack = new ItemStack(other, 1);
        }
        if (reason == "attributes") source.Itemstack!.Attributes.SetString("marker", "different");
        if (reason == "putlocked") test.Target.PutLocked = true;
        if (reason == "takelocked") test.Fixture.BackpackInventory.TakeLocked = true;
        if (reason == "rejected") source.Reject = true;
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        Assert.Equal(0, Execute(test).MovedQuantity);

        before.AssertUnchangedExcept();
        before.AssertConserved();
    }

    [Fact]
    public void LiveInputChanges_DoNotBroadenSnapshotOrFallBackToOutput()
    {
        var test = CreateTransfer();
        test.Target[0].Itemstack = new ItemStack(test.Item, 1);
        var source = test.Source(3);
        var policy = new QuernPolicy(test.Fixture.World, test.Target);
        var pass = Assert.Single(policy.GetSourcePasses(test.Fixture.BackpackInventory, null));
        Assert.Equal(3, policy.GetQuantity(source, pass));
        test.Target[0].Itemstack = null;
        Assert.Equal(0, policy.GetQuantity(source, pass));
        test.Target[0].Itemstack = new ItemStack(new MockItem(2, api: test.Fixture.Api)
            { GrindingProps = new GrindingProperties() }, 1);
        Assert.Equal(0, policy.GetQuantity(source, pass));
        Assert.False(policy.IsEligible(Assert.IsType<ItemStack>(test.Target[0].Itemstack), pass));
        test.Target[0].Itemstack = new ItemStack(test.Item, 64);
        Assert.Equal(0, policy.GetQuantity(source, pass));
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(64, true, false)]
    [InlineData(0, false, false)]
    public void Assessment_SeparatesCandidatesFromCapacity(int input, bool candidates, bool available)
    {
        var test = CreateTransfer();
        if (input > 0) test.Target[0].Itemstack = new ItemStack(test.Item, input);
        test.Target[1].Itemstack = new ItemStack(test.Item, 1);
        test.Source(3);
        var quern = CreateQuern(test);

        var assessment = AutoStashService.AssessBlock(test.Fixture.Player, quern.Object, true);

        Assert.Equal(candidates, assessment.CandidateItemIds.Count > 0);
        Assert.Equal(available, assessment.AvailableItemIds.Count > 0);
        test.AssertSessions(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServerDispatch_ReassessesCurrentInputAndFinalizesOnlyInputChanges(bool allowed)
    {
        var test = CreateTransfer();
        test.Target[0].Itemstack = new ItemStack(test.Item, 1);
        var source = test.Source(5);
        var quern = CreateQuern(test);
        using var request = new ServerRequestCase(test.Fixture);
        quern.Object.Pos = request.Position;
        var block = new BlockQuern();
        block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(quern.Object);
        request.Claims.Setup(value => value.TryAccess(request.Player.Object, request.Position, EnumBlockAccessFlags.Use))
            .Returns(allowed);
        Assert.True(AutoStashService.AssessBlock(test.Fixture.Player, quern.Object, true).HasCandidates);
        test.Target[0].Itemstack = new ItemStack(test.Item, 62);

        request.BlockRequest();

        Assert.Equal(allowed ? 64 : 62, test.Target[0].StackSize);
        Assert.True(test.Target[1].Empty);
        Assert.Equal(allowed ? 3 : 5, source.StackSize);
        Assert.Equal(allowed, test.ModifiedSlots.Contains(0));
        Assert.DoesNotContain(1, test.ModifiedSlots);
        quern.Verify(value => value.MarkDirty(false, null), allowed ? Times.Once() : Times.Never());
        request.AssertNoSyntheticTransfer();
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("open")]
    [InlineData("disabled")]
    [InlineData("empty")]
    public void GestureAndHelp_ExcludeVanillaGrindingAndIneligibleInteractions(string reason)
    {
        using var test = new BlockGestureCase("quern");
        if (reason == "upper") test.Selection.SelectionBoxIndex = 1;
        if (reason == "open")
            test.Fixture.InventoryManagerMock.SetupGet(value => value.OpenedInventories).Returns([test.TargetInventory]);
        if (reason == "disabled") VanillaExpandedModSystem.Config.EnableAutoStash = false;
        if (reason == "empty") test.TargetInventory[0].Itemstack = null;

        Assert.Equal(EnumHandling.PassThrough, test.Start().Handling);
        EnumHandling handling = EnumHandling.PassThrough;
        Assert.Empty(test.Behavior.GetPlacedBlockInteractionHelp(test.Fixture.World,
            test.Selection, test.Fixture.Player, ref handling));
        Assert.False(test.Behavior.HasActiveGesture(test.Fixture.World));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gesture_KeepsOwnershipWhenGuiOpensAndCleansUp(bool cancel)
    {
        using var test = new BlockGestureCase("quern");
        Assert.Equal(EnumHandling.PreventDefault, test.Start().Handling);
        test.TargetInventory.Open(test.Player.Object);
        test.Fixture.InventoryManagerMock.SetupGet(value => value.OpenedInventories).Returns([test.TargetInventory]);
        bool result = false;
        Assert.False(AutoStashPatch.QuernStep((BlockQuern)test.Behavior.block, .5f,
            test.Fixture.World, test.Player.Object, test.Selection, ref result));
        Assert.True(result);
        test.Step(.6f);
        test.AssertSubmission(1);
        test.End(cancel);
        Assert.False(test.Behavior.HasActiveGesture(test.Fixture.World));
        test.ProgressProvider.Verify(value => value.RemoveProgressBar(test.Progress.Object), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativeQuern_PreservesMachineStateAndSessionOwnership(bool powered, bool alreadyOpen)
    {
        var fixture = VsTestFixture.Server();
        var quern = new Mock<TestQuern>(fixture.Api, fixture.InvNetworkUtilMock.Object, powered) { CallBase = true };
        quern.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
        var item = new MockItem(1, api: fixture.Api)
        {
            Code = new AssetLocation("game:grain"), MaxStackSize = 64, GrindingProps = new GrindingProperties()
        };
        quern.Object.Inventory[0].Itemstack = new ItemStack(item, 62);
        quern.Object.Inventory[1].Itemstack = new ItemStack(item, 10);
        fixture.BackpackInventory[0].Itemstack = new ItemStack(item, 5);
        quern.Object.inputGrindTime = 2;
        var modified = new List<int>();
        quern.Object.Inventory.SlotModified += modified.Add;
        if (alreadyOpen)
            fixture.InventoryManagerMock.SetupGet(value => value.OpenedInventories).Returns([quern.Object.Inventory]);

        var result = AutoStashService.StashQuern(fixture.World, fixture.Player, quern.Object, "test");

        Assert.Equal(2, result.MovedQuantity);
        Assert.Equal(64, quern.Object.Inventory[0].StackSize);
        Assert.Equal(10, quern.Object.Inventory[1].StackSize);
        Assert.Equal(2, quern.Object.inputGrindTime);
        Assert.Equal(powered, quern.Object.IsPowered);
        Assert.Equal(0, quern.Object.GrindingPlayers);
        Assert.Contains(0, modified);
        Assert.DoesNotContain(1, modified);
        quern.Verify(value => value.MarkDirty(false, null), Times.Exactly(2));
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(quern.Object.Inventory),
            alreadyOpen ? Times.Never() : Times.Once());
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(quern.Object.Inventory),
            alreadyOpen ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Help_UsesInputCapacityAndPreservesVanillaActions(bool full)
    {
        using var test = new BlockGestureCase("quern");
        test.TargetInventory[0].Itemstack!.StackSize = full ? 64 : 62;
        var block = test.Behavior.block;
        var help = block.GetPlacedBlockInteractionHelp(test.Fixture.World, test.Selection, test.Player.Object);

        Assert.Contains(help, value => value.ActionLangCode == "blockhelp-quern-addremoveitems");
        Assert.Contains(help, value => value.ActionLangCode == (full
            ? "vanillaexpanded:blockhelp-autostash-full" : "vanillaexpanded:blockhelp-autostash-container"));
        test.Selection.SelectionBoxIndex = 1;
        help = block.GetPlacedBlockInteractionHelp(test.Fixture.World, test.Selection, test.Player.Object);
        Assert.Contains(help, value => value.ActionLangCode == "blockhelp-quern-grind");
        Assert.DoesNotContain(help, value => value.ActionLangCode.StartsWith("vanillaexpanded:blockhelp-autostash"));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("cancel")]
    [InlineData("disabled")]
    [InlineData("complete")]
    public void GestureTermination_ClearsOwnershipAndAllowsFreshStart(string reason)
    {
        using var test = new BlockGestureCase("quern");
        test.Start();
        bool result = false;
        var block = (BlockQuern)test.Behavior.block;
        AutoStashPatch.QuernStep(block, .1f, test.Fixture.World, test.Player.Object, test.Selection, ref result);
        switch (reason)
        {
            case "short":
                Assert.False(AutoStashPatch.QuernStop(block, .1f, test.Fixture.World, test.Player.Object, test.Selection));
                break;
            case "cancel":
                Assert.False(AutoStashPatch.QuernCancel(block, .1f, test.Fixture.World, test.Player.Object,
                    test.Selection, ref result));
                Assert.True(result);
                break;
            case "disabled":
                VanillaExpandedModSystem.Config.EnableAutoStash = false;
                Assert.False(AutoStashPatch.QuernStep(block, .5f, test.Fixture.World, test.Player.Object,
                    test.Selection, ref result));
                Assert.False(result);
                VanillaExpandedModSystem.Config.EnableAutoStash = true;
                break;
            case "complete":
                AutoStashPatch.QuernStep(block, .5f, test.Fixture.World, test.Player.Object, test.Selection, ref result);
                AutoStashPatch.QuernStep(block, 1f, test.Fixture.World, test.Player.Object, test.Selection, ref result);
                Assert.False(result);
                Assert.True(test.Behavior.HasActiveGesture(test.Fixture.World));
                Assert.False(AutoStashPatch.QuernStop(block, 1f, test.Fixture.World, test.Player.Object, test.Selection));
                break;
        }
        Assert.False(test.Behavior.HasActiveGesture(test.Fixture.World));
        Assert.Equal(reason == "complete" ? 1 : 0, test.Requests.Count);
        test.ProgressProvider.Verify(value => value.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Start();
        test.Step(.25f);
        Assert.Equal(reason == "complete" ? 1 : 0, test.Requests.Count);
        test.Step(.5f);
        Assert.Equal(reason == "complete" ? 2 : 1, test.Requests.Count);
    }

    [Fact]
    public void SessionOpeningCannotChangeTheOperationInputType()
    {
        var test = CreateTransfer();
        test.Target[0].Itemstack = new ItemStack(test.Item, 1);
        test.Source(3);
        var other = new MockItem(2, api: test.Fixture.Api)
            { Code = new AssetLocation("game:other"), GrindingProps = new GrindingProperties() };
        var otherSource = test.Source(3, 1, item: other);
        test.Fixture.InventoryManagerMock.Setup(value => value.OpenInventory(test.Target))
            .Callback(() => test.Target[0].Itemstack = new ItemStack(other, 1));

        Assert.Equal(0, Execute(test).MovedQuantity);

        Assert.Equal(1, test.Target[0].StackSize);
        Assert.True(test.Target[1].Empty);
        Assert.Equal(3, otherSource.StackSize);
    }

    [Fact]
    public void ServerExecution_UsesNewInputRatherThanPreviousAssessment()
    {
        var test = CreateTransfer();
        test.Target[0].Itemstack = new ItemStack(test.Item, 1);
        var originalSource = test.Source(3);
        var quern = CreateQuern(test);
        Assert.True(AutoStashService.AssessBlock(test.Fixture.Player, quern.Object, true).HasCandidates);
        var other = new MockItem(2, api: test.Fixture.Api)
            { Code = new AssetLocation("game:other"), GrindingProps = new GrindingProperties(), MaxStackSize = 64 };
        test.Target[0].Itemstack = new ItemStack(other, 1);
        var newSource = test.Source(3, 1, item: other);

        Assert.Equal(3, AutoStashService.StashQuern(test.Fixture.World, test.Fixture.Player, quern.Object, "test").MovedQuantity);
        Assert.Equal(3, originalSource.StackSize);
        Assert.True(newSource.Empty);
        Assert.Equal(4, test.Target[0].StackSize);
        Assert.True(test.Target[1].Empty);
    }

    public class TestQuern : BlockEntityQuern
    {
        public bool IsPowered => automated;
        public int GrindingPlayers => quantityPlayersGrinding;

        public TestQuern(ICoreAPI api, IInventoryNetworkUtil network, bool powered)
        {
            Api = api;
            Pos = new BlockPos(1, 2, 3);
            inventory.Api = api;
            inventory.InvNetworkUtil = network;
            automated = powered;
        }
    }

    private static TransferCase CreateTransfer()
    {
        var test = new TransferCase();
        test.Item.GrindingProps = new GrindingProperties();
        return test;
    }

    private static AutoStashResult Execute(TransferCase test)
        => AutoStashService.Execute(test.Fixture.World, test.Fixture.Player, "test",
            new InventoryAutoStashTarget(test.Target), new BlockPos(0), "quern",
            new QuernPolicy(test.Fixture.World, test.Target));

    private static Mock<BlockEntityQuern> CreateQuern(TransferCase test)
    {
        var quern = new Mock<BlockEntityQuern> { CallBase = true };
        quern.Object.Api = test.Fixture.Api;
        quern.Object.Pos = new BlockPos(1, 2, 3);
        quern.SetupGet(value => value.Inventory).Returns(test.Target);
        quern.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
        return quern;
    }
}
