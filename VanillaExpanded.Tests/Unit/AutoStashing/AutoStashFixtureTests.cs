using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Validates fixture observation boundaries using real engine slot operations.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AutoStashFixtureTests
{
    #region Public API
    /// <summary>Proves snapshots detect changed attributes, quantities, collectible identity, and replaced slots.</summary>
    [Fact]
    public void Snapshot_DetectsMutationWithoutAliasingExpectedState()
    {
        var fixture = VsTestFixture.Server();
        var item = MockItem.CreateNonLightSource(1, fixture.Api);
        fixture.WithBackpackSlot(0, item, 3);
        ItemSlot slot = fixture.BackpackInventory[0];
        slot.Itemstack!.Attributes.SetString("grade", "original");
        var before = new InventorySnapshot(fixture.BackpackInventory);
        before.AssertUnchangedExcept();
        slot.Itemstack.Attributes.SetString("grade", "changed");
        Assert.NotNull(Record.Exception(() => before.AssertUnchangedExcept()));
        slot.Itemstack.Attributes.SetString("grade", "original");
        slot.Itemstack.TempAttributes.SetInt("marker", 1);
        Assert.NotNull(Record.Exception(() => before.AssertUnchangedExcept()));
        slot.Itemstack.TempAttributes.RemoveAttribute("marker");
        slot.Itemstack.StackSize++;
        Assert.NotNull(Record.Exception(() => before.AssertConserved()));
        slot.Itemstack.StackSize--;
        ItemStack original = slot.Itemstack;
        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(2, fixture.Api), 3);
        Assert.NotNull(Record.Exception(() => before.AssertUnchangedExcept()));
        slot.Itemstack = original;
        fixture.BackpackInventory[0] = new ItemSlot(fixture.BackpackInventory) { Itemstack = slot.Itemstack };
        Assert.NotNull(Record.Exception(() => before.AssertUnchangedExcept()));
    }

    /// <summary>Verifies quantity limits, rejected moves, and before/after callbacks retain real engine mutation.</summary>
    [Fact]
    public void ObservedSlot_ControlsBoundaryAndRecordsActualEngineMoves()
    {
        var fixture = VsTestFixture.Server();
        var item = MockItem.CreateNonLightSource(1, fixture.Api);
        var source = new ObservedTransferSlot(fixture.BackpackInventory) { Itemstack = new ItemStack(item, 5), QuantityLimit = 2 };
        fixture.BackpackInventory[0] = source;
        using var observation = new AutoStashObservation();
        source.BeforeMove = _ => observation.Record("before");
        source.AfterMove = _ => observation.Record("after");
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory);
        var op = new ItemStackMoveOperation(fixture.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, 5);
        Assert.Equal(2, source.TryPutInto(fixture.HotbarInventory[1], ref op));
        Assert.Equal(new[] { "before", "after" }, observation.Events);
        Assert.Equal(5, Assert.Single(source.Attempts).Quantity);
        InventorySnapshot.AssertStack(source, item, 3);
        InventorySnapshot.AssertStack(fixture.HotbarInventory[1], item, 2);
        before.AssertConserved();
        before.AssertUnchangedExcept(source, fixture.HotbarInventory[1]);
        source.Reject = true;
        Assert.Equal(0, source.TryPutInto(fixture.HotbarInventory[1], ref op));
        InventorySnapshot.AssertStack(source, item, 3);
        source.Reject = false;
        source.BeforeMove = _ => throw new InvalidOperationException("controlled");
        Assert.Throws<InvalidOperationException>(() => source.TryPutInto(fixture.HotbarInventory[1], ref op));
        source.AttemptLimit = source.Attempts.Count;
        Assert.Throws<InvalidOperationException>(() => source.TryPutInto(fixture.HotbarInventory[1], ref op));
    }

    /// <summary>Verifies deterministic tick/persistence observation and reverse cleanup despite a failing cleanup action.</summary>
    [Fact]
    public void Observation_CleansAllResourcesAfterFailure()
    {
        using var observation = new AutoStashObservation();
        observation.RegisterTick(_ => observation.Record("persist"), () => observation.Record("unregister"));
        observation.OnDispose(() => observation.Record("unpatch"));
        observation.OnDispose(() => throw new InvalidOperationException("cleanup"));
        observation.Tick(0.1f);
        Assert.Throws<AggregateException>(observation.Dispose);
        Assert.Equal(new[] { "register", "persist", "unpatch", "unregister" }, observation.Events);
        Assert.Throws<InvalidOperationException>(() => observation.Tick(0.1f));
        observation.Dispose();
    }

    /// <summary>Proves configuration cleanup restores both settings after an exceptional test exit.</summary>
    [Fact]
    public void ConfigurationScope_RestoresSettingsAfterException()
    {
        var config = VanillaExpandedModSystem.Config;
        bool enabled = config.EnableAutoStash;
        float delay = config.AutoStashDelay;
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = new AutoStashTestScope();
            config.EnableAutoStash = false;
            config.AutoStashDelay = 9;
            throw new InvalidOperationException("test exit");
        }));
        Assert.Equal(enabled, config.EnableAutoStash);
        Assert.Equal(delay, config.AutoStashDelay);
    }

    /// <summary>Verifies suitability observation delegates ranking and selection to the real engine inventory.</summary>
    [Fact]
    public void ObservedInventory_RecordsEngineSuitabilityQueries()
    {
        var fixture = VsTestFixture.Server();
        var item = MockItem.CreateNonLightSource(1, fixture.Api);
        fixture.WithBackpackSlot(0, item, 1);
        var target = new ObservedInventory(2, fixture.Api);
        target.OnGetSuitability = (_, slot, _) => ReferenceEquals(slot, target[1]) ? 9 : 1;
        WeightedSlot selected = target.GetBestSuitedSlot(fixture.BackpackInventory[0]);
        Assert.Same(target[1], selected.slot);
        Assert.Equal(new[] { target[0], target[1] }, target.RankedSlots);
        Assert.Equal(1, fixture.BackpackInventory[0].StackSize);
        Assert.True(target.Empty);
    }

    /// <summary>Verifies vanilla-initialized crate locks and actual slots through the AutoStash entry point.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializedCrate_UsesVanillaInventoryConfiguration(bool retrieveOnly)
    {
        var fixture = VsTestFixture.Server();
        var item = MockItem.CreateNonLightSource(1, fixture.Api);
        item.MaxStackSize = 64;
        fixture.WithBackpackSlot(0, item, 3);
        var crate = new InitializedCrate(fixture.Api, retrieveOnly);
        item.Code = new AssetLocation("game:crate-fixture-item");
        Assert.IsType<InventoryGeneric>(crate.Inventory);
        Assert.Equal(2, crate.Inventory.Count);
        Assert.Equal(retrieveOnly, crate.Inventory.PutLocked);
        Assert.NotNull(((InventoryGeneric)crate.Inventory).OnGetAutoPushIntoSlot);
        crate.Inventory[0].Itemstack = new ItemStack(item, 1);
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, crate.Inventory);
        int dirtyBefore = crate.DirtyCalls;
        bool moved = BlockBehaviorAutoStashable.AutoStashToCrate(fixture.World, fixture.Player, crate);
        Assert.Equal(!retrieveOnly, moved);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(crate.Inventory), retrieveOnly ? Times.Never() : Times.Once());
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(crate.Inventory), retrieveOnly ? Times.Never() : Times.Once());
        before.AssertConserved();
        if (retrieveOnly)
        {
            before.AssertUnchangedExcept();
            Assert.Equal(dirtyBefore, crate.DirtyCalls);
        }
        else
        {
            before.AssertUnchangedExcept(fixture.BackpackInventory[0], crate.Inventory[0]);
            Assert.True(fixture.BackpackInventory[0].Empty);
            InventorySnapshot.AssertStack(crate.Inventory[0], item, 4);
            Assert.True(crate.DirtyCalls > dirtyBefore);
        }
    }

    /// <summary>Separates code-only eligibility from existing-stack merging using deliberately different item identities.</summary>
    [Fact]
    public void GenericContainer_CodeOnlyEligibility_UsesEmptySlotWithoutReplacingExistingIdentity()
    {
        var fixture = VsTestFixture.Server();
        var existing = MockItem.CreateNonLightSource(1, fixture.Api);
        var candidate = MockItem.CreateNonLightSource(2, fixture.Api);
        existing.Code = candidate.Code = new AssetLocation("game:code-only");
        existing.MaxStackSize = 1;
        fixture.WithBackpackSlot(0, candidate, 1);
        var container = MockBlockEntityContainer.WithItems(new Dictionary<int, MockItem> { [0] = existing }, 2, api: fixture.Api);
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);
        Assert.True(BlockBehaviorAutoStashable.AutoStashToGenericContainer(fixture.World, fixture.Player, container.Object));
        InventorySnapshot.AssertStack(container.Inventory[0], existing, 1);
        InventorySnapshot.AssertStack(container.Inventory[1], candidate, 1);
        Assert.True(fixture.BackpackInventory[0].Empty);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], container.Inventory[1]);
        before.AssertConserved();
    }
    #endregion
}
