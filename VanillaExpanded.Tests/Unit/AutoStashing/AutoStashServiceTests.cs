using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.AutoStashing.Targets;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects truthful common operation results independently of compatibility entry return values.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class AutoStashServiceTests
{
    #region Public API
    #region Operation outcomes
    /// <summary>Distinguishes unavailable, absent candidates, full destinations and actual partial movement.</summary>
    [Theory]
    [InlineData("unavailable", 0, 0)]
    [InlineData("candidate", 1, 0)]
    [InlineData("full", 2, 0)]
    [InlineData("rejected", 2, 0)]
    [InlineData("partial", 3, 2)]
    public void CompletedResult_ReportsActualOutcome(string scenario, int outcome, int moved)
    {
        var test = new TransferCase(1);
        test.Target[0].Itemstack = new ItemStack(test.Item, scenario == "full" ? 64 : 62);
        var source = test.Source(5);
        source.Reject = scenario == "rejected";
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        var policy = new MatchingContentsPolicy(_ => scenario != "candidate");

        var result = AutoStashService.Execute(test.Fixture.World, test.Fixture.Player, "test",
            scenario == "unavailable" ? null : new InventoryAutoStashTarget(test.Target), new BlockPos(0), "test", policy);

        Assert.Equal((AutoStashOutcome)outcome, result.Outcome);
        Assert.Equal(moved, result.MovedQuantity);
        Assert.Equal(scenario == "rejected", result.DirectMergeFailed);
        InventorySnapshot.AssertStack(source, test.Item, 5 - moved);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, (scenario == "full" ? 64 : 62) + moved);
        before.AssertUnchangedExcept(moved > 0 ? [source, test.Target[0]] : []);
        before.AssertConserved();
        test.AssertSessions(scenario is "partial" or "rejected" ? 1 : 0);
    }

    /// <summary>Reports exhausted direct failure alongside partial success, after finalization and owned cleanup.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectFailure_IsIndependentOfMovement(bool partial)
    {
        var test = new TransferCase(1);
        var item = new DeferredMergeItem(test.Fixture.Api) { RejectDirect = !partial, RepeatRequiredPriority = true };
        var source = test.Source(5, item: item);
        source.QuantityLimit = 2;
        source.AttemptLimit = 5;
        source.AfterMove = _ => { if (partial && source.Attempts.Last().Priority == EnumMergePriority.DirectMerge) source.Reject = true; };
        test.Target[0].Itemstack = new ItemStack(item, 1);
        var errors = new TransferErrorObservation(test);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = AutoStashService.Execute(test.Fixture.World, test.Fixture.Player, "test",
            new InventoryAutoStashTarget(test.Target), new BlockPos(0), "test", new MatchingContentsPolicy(_ => true));

        Assert.Equal(partial ? 2 : 0, result.MovedQuantity);
        Assert.Equal(partial ? AutoStashOutcome.Success : AutoStashOutcome.NoDestination, result.Outcome);
        Assert.True(result.DirectMergeFailed);
        InventorySnapshot.AssertStack(source, item, partial ? 3 : 5);
        InventorySnapshot.AssertStack(test.Target[0], item, partial ? 3 : 1);
        before.AssertUnchangedExcept(partial ? [source, test.Target[0]] : []);
        before.AssertConserved();
        errors.AssertErrors(1);
        test.AssertSessions(1);
    }

    /// <summary>Recognizes valid bloomery inputs despite exhausted capacity, without opening a session or mutating.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BloomeryResult_SeparatesCandidatesAndCapacity(bool valid)
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.TestInventory[1], test.Ore, 12);
        test.Source(valid ? test.Ore : test.Invalid, 3);
        var before = test.Snapshot();

        var result = AutoStashService.StashBloomery(test.Fixture.World, test.Fixture.Player, test.Target, "test");

        Assert.Equal(valid ? AutoStashOutcome.NoDestination : AutoStashOutcome.NoCandidates, result.Outcome);
        Assert.Equal(0, result.MovedQuantity);
        Assert.False(result.DirectMergeFailed);
        test.Verify(before, false);
    }
    #endregion

    #region Target coordination
    /// <summary>Preserves bloomery audit templates and counts actual ore plus ratio-limited fuel.</summary>
    [Fact]
    public void BloomerySuccess_UsesCommonResultAndExistingAudits()
    {
        var test = new BloomeryCase();
        test.Fixture.WorldMock.Setup(world => world.Api).Returns(test.Fixture.Api);
        var ore = test.Source(test.Ore, 5);
        var fuel = test.Source(test.Fuel, 5, 1);
        var before = test.Snapshot();

        var result = AutoStashService.StashBloomery(test.Fixture.World, test.Fixture.Player, test.Target, "test");

        Assert.Equal(new AutoStashResult(8, AutoStashOutcome.Success), result);
        test.AssertSlot(ore, test.Ore, 0);
        test.AssertSlot(fuel, test.Fuel, 2);
        test.AssertSlot(test.Target.TestInventory[1], test.Ore, 5);
        test.AssertSlot(test.Target.TestInventory[0], test.Fuel, 3);
        test.Fixture.LoggerMock.Verify(logger => logger.Audit("'{0}' moved {1}x{2} into bloomery at <{3}>.",
            It.Is<object[]>(args => (int)args[1] == 5 && Equals(args[2], test.Ore.Code))), Times.Once);
        test.Fixture.LoggerMock.Verify(logger => logger.Audit("'{0}' moved {1}x{2} into bloomery at <{3}>.",
            It.Is<object[]>(args => (int)args[1] == 3 && Equals(args[2], test.Fuel.Code))), Times.Once);
        test.Fixture.LoggerMock.Verify(logger => logger.Audit("'{0}' auto-stashed {1} items into bloomery at <{2}>.",
            It.Is<object[]>(args => (int)args[1] == 8)), Times.Once);
        test.Verify(before, true, ore, fuel, test.Target.TestInventory[0], test.Target.TestInventory[1]);
    }

    /// <summary>Returns actual attached movement and persists both bag implementations through the common coordinator.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttachedResult_PersistsActualPartialMovement(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 62), test.Stack(test.Unrelated, 64));
        var source = test.Fixture.BackpackInventory[0];
        source.Itemstack = test.Stack(test.Item, 5);
        var before = test.Snapshot();

        var result = AutoStashService.StashAttached(test.Fixture.World, test.Player,
            test.Host, test.AttachmentMock.Object, 0);

        Assert.Equal(new AutoStashResult(2, AutoStashOutcome.Success), result);
        InventorySnapshot.AssertStack(source, test.Item, 3);
        test.AssertPersisted(64, 64);
        test.Verify(before, true, vanilla ? 1 : 0, source);
    }
    #endregion
    #endregion
}
