using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects advisory certainty, legacy client matching and read-only policy reuse without a game runtime.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AutoStashAssessmentTests
{
    #region Public API
    #region Block assessment
    /// <summary>Separates matching candidates from automatic capacity without movement, dirty effects or sessions.</summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("unmatched")]
    [InlineData("full")]
    [InlineData("available")]
    public void ContainerAssessment_ReportsCapacityWithoutMutations(string state)
    {
        var test = new TransferCase(1);
        if (state != "empty") test.Target[0].Itemstack = new ItemStack(test.Item, state == "full" ? 64 : 10);
        var source = test.Source(3);
        var target = new Mock<BlockEntityContainer>();
        target.SetupGet(value => value.Inventory).Returns(test.Target);
        if (state == "unmatched")
            source.Itemstack = new ItemStack(new MockItem(2, api: test.Fixture.Api) { Code = new AssetLocation("game:other") }, 3);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = AutoStashService.AssessBlock(test.Fixture.Player, target.Object, interactionAllowed: true);

        Assert.Equal(state is "full" or "available", result.HasCandidates);
        Assert.Equal(state == "available" ? AutoStashCapacity.Available : AutoStashCapacity.Unavailable, result.Capacity);
        Assert.Equal(state == "available" ? new[] { test.Item.Id } : [], result.AvailableItemIds);
        if (state == "available") Assert.NotSame(source.Itemstack, Assert.Single(result.DisplayStacks));
        else Assert.Empty(result.DisplayStacks);
        Assert.Empty(source.Attempts);
        Assert.Empty(test.ModifiedSlots);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        test.AssertSessions(0);
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
    }

    /// <summary>Advisory automatic capacity stays unavailable when only a direct destination is engine-eligible.</summary>
    [Fact]
    public void DirectOnlyDestination_DoesNotBroadenClientCapacity()
    {
        var test = new TransferCase(1);
        var destination = new Mock<ItemSlot>(test.Target) { CallBase = true };
        destination.Object.Itemstack = new ItemStack(test.Item, 10);
        destination.Setup(slot => slot.CanTakeFrom(It.IsAny<ItemSlot>(), It.IsAny<EnumMergePriority>()))
            .Returns<ItemSlot, EnumMergePriority>((_, priority) => priority == EnumMergePriority.DirectMerge);
        test.Target[0] = destination.Object;
        var source = test.Source(3);
        var policy = MatchingContentsPolicy.ForAssessment([destination.Object.Itemstack]);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = AutoStashAssessor.Assess(policy, test.Target, test.Fixture.BackpackInventory, test.Fixture.HotbarInventory);

        Assert.True(result.HasCandidates);
        Assert.Equal(AutoStashCapacity.Unavailable, result.Capacity);
        Assert.Empty(result.AvailableItemIds);
        Assert.Empty(result.DisplayStacks);
        Assert.True(AutoStashPlanner.HasWork(test.Target, policy, test.Fixture.BackpackInventory, test.Fixture.HotbarInventory));
        Assert.Empty(source.Attempts);
        Assert.Empty(test.ModifiedSlots);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        test.AssertSessions(0);
    }

    /// <summary>Retains ID-based client membership and all-content crate help independently of server execution policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientMatching_UsesIdsAndAllCurrentContents(bool crate)
    {
        var test = new TransferCase(3);
        var second = new MockItem(2, api: test.Fixture.Api) { Code = new AssetLocation("game:second"), MaxStackSize = 64 };
        var sameCode = new MockItem(3, api: test.Fixture.Api) { Code = test.Item.Code, MaxStackSize = 64 };
        test.Target[0].Itemstack = new ItemStack(test.Item, 1);
        test.Target[1].Itemstack = new ItemStack(second, 1);
        var accepted = test.Source(3, item: second);
        var rejected = test.Source(4, 1, item: sameCode);
        BlockEntityContainer target;
        if (crate)
        {
            var mock = new Mock<BlockEntityCrate>();
            mock.SetupGet(value => value.Inventory).Returns(test.Target);
            target = mock.Object;
        }
        else
        {
            var mock = new Mock<BlockEntityContainer>();
            mock.SetupGet(value => value.Inventory).Returns(test.Target);
            target = mock.Object;
        }
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);

        var result = AutoStashService.AssessBlock(test.Fixture.Player, target, true);

        Assert.Equal(new[] { second.Id }, result.CandidateItemIds);
        Assert.Equal(new[] { second.Id }, result.AvailableItemIds);
        Assert.Same(second, Assert.Single(result.DisplayStacks).Collectible);
        // Execution's code identity still includes this source; advisory IDs must not be silently normalized.
        Assert.True(new MatchingContentsPolicy([test.Target[0].Itemstack]).IsEligible(rejected.Itemstack!, new AutoStashSourcePass(test.Fixture.BackpackInventory)));
        Assert.Empty(accepted.Attempts);
        Assert.Empty(rejected.Attempts);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        test.AssertSessions(0);
    }

    /// <summary>Retains backpack slot order for help rather than ore/fuel execution-pass order, with independent clones.</summary>
    [Fact]
    public void BloomeryDisplay_UsesSourceOrderAndIndependentRepresentatives()
    {
        var test = new BloomeryCase();
        test.Seed(test.Target.TestInventory[1], test.Ore, 1);
        var fuel = test.Source(test.Fuel, 1);
        var ore = test.Source(test.Ore, 3, 1);
        test.Source(test.Fuel, 2, 0, true);
        var before = test.Snapshot();

        var first = AutoStashService.AssessBlock(test.Fixture.Player, test.Target, true);
        var second = AutoStashService.AssessBlock(test.Fixture.Player, test.Target, true);

        Assert.Equal(AutoStashCapacity.Available, first.Capacity);
        Assert.Equal(new[] { test.Fuel.Id, test.Ore.Id }, first.DisplayStacks.Select(stack => stack.Collectible.Id));
        Assert.Equal(new[] { 1, 3 }, first.DisplayStacks.Select(stack => stack.StackSize));
        Assert.NotSame(fuel.Itemstack, first.DisplayStacks[0]);
        Assert.NotSame(ore.Itemstack, first.DisplayStacks[1]);
        Assert.NotSame(first.DisplayStacks[0], second.DisplayStacks[0]);
        first.DisplayStacks[0].StackSize = 99;
        first.DisplayStacks[0].Attributes.SetString("fixture", "changed");
        Assert.Equal("preserved", second.DisplayStacks[0].Attributes.GetString("fixture"));
        test.Verify(before, false);
        Assert.Empty(ore.Attempts);
        Assert.Empty(fuel.Attempts);
    }

    /// <summary>The explicit client gate can hide an empty bloomery without becoming a server execution prerequisite.</summary>
    [Fact]
    public void EmptyBloomeryGate_IsAdvisoryInputOnly()
    {
        var test = new BloomeryCase();
        var ore = test.Source(test.Ore, 3);
        var before = test.Snapshot();

        var blocked = AutoStashService.AssessBlock(test.Fixture.Player, test.Target, interactionAllowed: false);
        var allowed = AutoStashService.AssessBlock(test.Fixture.Player, test.Target, interactionAllowed: true);

        Assert.False(blocked.HasCandidates);
        Assert.Equal(AutoStashCapacity.Unavailable, blocked.Capacity);
        Assert.True(allowed.HasCandidates);
        Assert.Equal(AutoStashCapacity.Available, allowed.Capacity);
        test.Verify(before, false);
        var moved = AutoStashService.StashBloomery(test.Fixture.World, test.Fixture.Player, test.Target, "test");
        Assert.Equal(3, moved.MovedQuantity);
        Assert.True(ore.Empty);
        test.AssertSlot(test.Target.TestInventory[1], test.Ore, 3);
        test.Verify(before, true, ore, test.Target.TestInventory[1]);
    }
    #endregion

    #region Contents-only assessment
    /// <summary>Full bags still expose matching candidates with unknown capacity and never load or persist execution slots.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttachedAssessment_LeavesCapacityUnknownAndWorkspaceUntouched(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 64), test.Stack(test.Unrelated, 64));
        var source = test.Fixture.BackpackInventory[0];
        source.Itemstack = test.Stack(test.Item, 3);
        var before = test.Snapshot();

        var result = AutoStashService.AssessAttached(test.Fixture.World, test.Fixture.Player, test.Attachments[0]);

        Assert.True(result.HasCandidates);
        Assert.Equal(AutoStashCapacity.Unknown, result.Capacity);
        Assert.Empty(result.AvailableItemIds);
        Assert.NotSame(source.Itemstack, Assert.Single(result.DisplayStacks));
        result.DisplayStacks[0].Attributes.SetString("fixture", "display changed");
        Assert.Null(test.Workspace);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
        test.BagMock.Verify(bag => bag.Store(It.IsAny<ItemStack>(), It.IsAny<ItemSlotBagContent>()), Times.Never);
        test.Verify(before, false, 0);
    }

    /// <summary>Missing inventories and contents-only views retain absence of candidates without fabricating available space.</summary>
    [Fact]
    public void MissingSources_DoNotClaimCapacityForBagView()
    {
        var result = AutoStashAssessor.Assess(MatchingContentsPolicy.ForAssessment([]), null, null, null);
        Assert.False(result.HasCandidates);
        Assert.Equal(AutoStashCapacity.Unknown, result.Capacity);
        Assert.Empty(result.AvailableItemIds);
        Assert.Empty(result.DisplayStacks);
    }
    #endregion
    #endregion
}
