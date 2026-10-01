using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects persistent bag reloads and the separate client candidate assessment contract.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class EntityAttachedContainerPersistenceTests
{
    #region Public API
    /// <summary>Reloads owner-persisted bag data, transfers again and preserves existing workspace slot identities.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RepeatedOperations_ReloadPersistedContentsWithoutDuplication(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 10), test.Stack(test.Unrelated, 4));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        var before = test.Snapshot();
        Assert.True(test.Run());
        test.AssertPersisted(17, 4);
        test.Verify(before, true, vanilla ? 1 : 0, test.Fixture.BackpackInventory[0]);
        ItemSlot[]? originalSlots = test.Workspace?.WrapperInv.ToArray();

        // Replace the attachment with a deserialized owner copy and deliberately poison old workspace contents.
        // The next complete operation must reload storage, rather than use those stale contents.
        test.ReloadOwnerBag();
        if (vanilla)
        {
            test.Workspace!.WrapperInv[0].Itemstack = test.Stack(test.Item, 63);
            test.Workspace.WrapperInv[1].Itemstack = null;
        }
        test.ClearObservations();
        test.Fixture.HotbarInventory[0].Itemstack = test.Stack(test.Item, 5);
        var reloaded = test.Snapshot();

        Assert.True(test.Run());

        Assert.True(test.Fixture.HotbarInventory[0].Empty);
        test.AssertPersisted(22, 4);
        test.Verify(reloaded, true, vanilla ? 1 : 0, test.Fixture.HotbarInventory[0]);
        if (vanilla)
        {
            Assert.Equal(originalSlots!.Length, test.Workspace!.WrapperInv.Count);
            for (int index = 0; index < originalSlots.Length; index++)
                Assert.Same(originalSlots[index], test.Workspace.WrapperInv[index]);
            InventorySnapshot.AssertStack(test.Workspace.WrapperInv[0], test.Item, 22);
            InventorySnapshot.AssertStack(test.Workspace.WrapperInv[1], test.Unrelated, 4);
        }

        test.ReloadOwnerBag();
        test.ClearObservations();
        var exhausted = test.Snapshot();
        Assert.False(test.Run());
        test.AssertPersisted(22, 4);
        test.Verify(exhausted, false, 0);
        if (vanilla)
            for (int index = 0; index < originalSlots!.Length; index++)
                Assert.Same(originalSlots[index], test.Workspace!.WrapperInv[index]);
    }

    /// <summary>Candidate assessment accepts matching full bags without promising capacity or performing lifecycle work.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullBag_CandidateAssessmentDoesNotPromiseExecutionCapacity(bool vanilla)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(test.Stack(test.Item, 64), test.Stack(test.Unrelated, 64));
        test.Fixture.BackpackInventory[0].Itemstack = test.Stack(test.Item, 7);
        var before = test.Snapshot();

        Assert.True(EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));

        test.Verify(before, false, 0);
        Assert.Null(test.Workspace);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
        Assert.False(test.Run());
        test.Verify(before, false, 0);
    }

    /// <summary>Candidate detection uses hotbar sources and rejects empty/nonmatching bags without loading workspaces.</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void CandidateAssessment_IsObservational(bool vanilla, bool populated, bool matching)
    {
        var test = new AttachedContainerCase(vanilla);
        test.SeedContents(populated ? test.Stack(test.Item, 10) : null, null);
        test.Fixture.HotbarInventory[0].Itemstack = test.Stack(matching ? test.Item : test.Other, 7);
        var before = test.Snapshot();

        Assert.Equal(populated && matching,
            EntityAttachedContainerAutoStash.CanAutoStash(test.Fixture.Player, test.Attachments[0], test.Fixture.World));

        test.Verify(before, false, 0);
        Assert.Null(test.Workspace);
        test.BagMock.Verify(bag => bag.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(),
            It.IsAny<int>(), It.IsAny<IWorldAccessor>()), Times.Never);
    }
    #endregion
}
