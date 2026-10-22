using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks coherent regional/terrain admission, shared-source reuse and cache hits after budget exhaustion.</summary>
public sealed class BudgetedGraphicsTests
{
    #region Public API
    /// <summary>Insufficient quota publishes neither missing component, while an existing receipt remains ready.</summary>
    [Fact]
    public void RegionalAndTerrainReadinessAreAdmittedTogether()
    {
        using var fixture = new BudgetedGraphicsFixture();
        var first = fixture.Receipt(0); var next = fixture.Receipt(16, true);
        var budget = new HabitatUploadBudget(new(long.MaxValue, 5, TimeSpan.FromHours(1)));
        Assert.True(fixture.Graphics.TryPrepare(first, budget));
        Assert.Equal(4, budget.Used.Operations); // Two new pages and their two source transfers.
        Assert.False(fixture.Graphics.TryPrepare(next, budget)); // Two missing transfers cannot fit the last operation.
        Assert.Equal(1, fixture.RegionCount); Assert.Equal(1, fixture.TileCount); Assert.Equal(2, fixture.Transfers);
        Assert.True(fixture.Graphics.TryPrepare(first, budget));
        Assert.Equal(4, budget.Used.Operations); Assert.Equal(2, fixture.Transfers);
        var following = new HabitatUploadBudget(new(long.MaxValue, 5, TimeSpan.FromHours(1)));
        Assert.True(fixture.Graphics.TryPrepare(next, following));
        Assert.Equal(2, following.Used.Operations);
        Assert.Equal(2, fixture.RegionCount); Assert.Equal(2, fixture.TileCount);
    }
    /// <summary>One oversized coherent receipt progresses per frame and shared regional data is never reuploaded for later tiles.</summary>
    [Fact]
    public void OversizedInputsEventuallyPopulateWithSharedRegionalReuse()
    {
        using var fixture = new BudgetedGraphicsFixture();
        var receipts = Enumerable.Range(0, 4).Select(x => fixture.Receipt(x)).ToArray();
        var limits = new HabitatUploadLimits(8, 1, TimeSpan.FromHours(1));
        for (int frame = 0; frame < receipts.Length; frame++)
        {
            var budget = new HabitatUploadBudget(limits);
            for (int i = 0; i < receipts.Length; i++)
                Assert.Equal(i <= frame, fixture.Graphics.TryPrepare(receipts[i], budget));
        }
        Assert.Equal(1, fixture.RegionCount); Assert.Equal(4, fixture.TileCount);
        Assert.Equal(5, fixture.Transfers);
    }
    #endregion
}
