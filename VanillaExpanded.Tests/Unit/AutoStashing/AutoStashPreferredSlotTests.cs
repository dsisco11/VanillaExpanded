using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Characterizes the currently unused preferred-slot argument and its differing preflight fallback.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class AutoStashPreferredSlotTests
{
    #region Public API
    /// <summary>Proves valid preferences select that slot while invalid indices use ordinary selection.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    public void PreferredIndex_SelectsValidSlotOrFallsBack(int preferred, int expected)
    {
        var test = new TransferCase();
        var source = test.Source(3);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(3, test.Run(_ => preferred));
        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[expected], test.Item, 3);
        snapshot.AssertUnchangedExcept(source, test.Target[expected]);
        snapshot.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        test.AssertSessions(1);
    }

    /// <summary>Records that blocked valid preferences reject preflight despite an otherwise viable alternative.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BlockedPreferredSlot_PreflightDoesNotFallBack(bool preferBlocked)
    {
        var test = new TransferCase();
        var source = test.Source(3);
        test.Target[0].Itemstack = new ItemStack(test.Item, 64);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(preferBlocked ? 0 : 3, test.Run(_ => preferBlocked ? 0 : null));
        if (preferBlocked)
        {
            snapshot.AssertUnchangedExcept();
            Assert.Empty(test.ModifiedSlots);
        }
        else
        {
            Assert.True(source.Empty);
            InventorySnapshot.AssertStack(test.Target[1], test.Item, 3);
            snapshot.AssertUnchangedExcept(source, test.Target[1]);
            Assert.NotEmpty(test.ModifiedSlots);
        }
        snapshot.AssertConserved();
        test.AssertSessions(preferBlocked ? 0 : 1);
    }

    /// <summary>Records execution fallback after a preferred slot accepts only part of a source stack.</summary>
    [Fact]
    public void PartiallyFilledPreferredSlot_ExecutionFallsBack()
    {
        var test = new TransferCase();
        var source = test.Source(3);
        test.Target[0].Itemstack = new ItemStack(test.Item, 63);
        var snapshot = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Target);
        Assert.Equal(3, test.Run(_ => 0));
        Assert.True(source.Empty);
        InventorySnapshot.AssertStack(test.Target[0], test.Item, 64);
        InventorySnapshot.AssertStack(test.Target[1], test.Item, 2);
        Assert.Equal(new[] { test.Target[0], test.Target[1] }, source.Attempts.Select(a => a.Destination));
        snapshot.AssertUnchangedExcept(source, test.Target[0], test.Target[1]);
        snapshot.AssertConserved();
        Assert.NotEmpty(test.ModifiedSlots);
        test.AssertSessions(1);
    }
    #endregion
}
