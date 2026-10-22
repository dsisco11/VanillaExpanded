using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Verifies deterministic byte/operation/time admission and the oversized-resource progress rule.</summary>
public sealed class HabitatUploadBudgetTests
{
    #region Public API
    /// <summary>Byte and operation ceilings independently stop further work while resident receipts remain admissible.</summary>
    [Theory]
    [InlineData(8, 100)]
    [InlineData(100, 2)]
    public void ByteAndOperationLimitsPreserveCachedDrawing(long bytes, int operations)
    {
        var time = new ManualUploadTime();
        var budget = new HabitatUploadBudget(new(bytes, operations, TimeSpan.FromMilliseconds(2)), time);
        var cost = new HabitatUploadCost(4, 0, 1, 0);
        Assert.True(budget.TryAdmit(cost)); Assert.True(budget.TryAdmit(cost));
        Assert.False(budget.TryAdmit(cost));
        time.Milliseconds = 100;
        Assert.True(budget.TryAdmit(default));
        Assert.Equal(new HabitatUploadCost(8, 0, 2, 0), budget.Used);
    }
    /// <summary>Clock expiration is observed between operations, without charging cached lookups or starting an unused clock.</summary>
    [Fact]
    public void ElapsedLimitStartsOnActualWorkAndStopsLaterAdmission()
    {
        var time = new ManualUploadTime();
        var limits = new HabitatUploadLimits(1024, 20, TimeSpan.FromMilliseconds(2));
        var budget = new HabitatUploadBudget(limits, time);
        Assert.True(budget.TryAdmit(default)); Assert.Equal(0, time.Reads);
        Assert.True(budget.TryAdmit(new(4, 0, 1, 0)));
        time.Milliseconds = 2;
        Assert.False(budget.TryAdmit(new(4, 0, 1, 0)));
        Assert.True(budget.TryAdmit(default));
        // A fresh frame resets all admission state even though the monotonic clock keeps advancing.
        Assert.True(new HabitatUploadBudget(limits, time).TryAdmit(new(4, 0, 1, 0)));
    }
    /// <summary>A coherent oversized first receipt can exceed normal limits once; it cannot unlock a second upload.</summary>
    [Fact]
    public void OversizedFirstAdmissionMakesProgressWithoutUnlimitedBurst()
    {
        var time = new ManualUploadTime();
        var budget = new HabitatUploadBudget(new(16, 1, TimeSpan.FromMilliseconds(2)), time);
        var cost = new HabitatUploadCost(4100, 1024 * 1024, 2, 2);
        Assert.True(budget.TryAdmit(cost));
        Assert.Equal(cost, budget.Used);
        Assert.False(budget.TryAdmit(new(4, 0, 1, 0)));
        Assert.True(budget.TryAdmit(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.TryAdmit(new(-1, 0, 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HabitatUploadLimits(0, 1, TimeSpan.FromMilliseconds(1)));
    }
    /// <summary>The cost forecast includes page storage and exactly matches the allocator's actual geometry.</summary>
    [Theory]
    [InlineData(false, 32)]
    [InlineData(true, 33)]
    public void AtlasEstimatesAllocationAndTransferWithoutReservingCells(bool normalized, int size)
    {
        using var atlas = new HabitatAtlas(normalized, 4096, 9, (w, h, n) => new AtlasTextureFixture());
        Array values = normalized ? new byte[size * size * 4] : new ushort[size * size * 2];
        var forecast = atlas.EstimateUpload(size);
        Assert.Equal(0, atlas.PageCount); Assert.Equal(0, atlas.Used);
        Assert.Equal((long)size * size * 4, forecast.TransferBytes);
        Assert.Equal(1, forecast.Allocations); Assert.Equal(1, forecast.Transfers);
        using var first = atlas.Upload(values, size);
        Assert.Equal(atlas.PayloadBytes, forecast.AllocationBytes);
        var retained = first.Address;
        var next = atlas.EstimateUpload(size);
        Assert.Equal(0, next.AllocationBytes); Assert.Equal(0, next.Allocations);
        Assert.Equal(forecast.TransferBytes, next.TransferBytes);
        Assert.Equal(retained, first.Address); Assert.Equal(1, atlas.Used);
    }
    #endregion
}

/// <summary>Provides deterministic monotonic timestamps without sleeping or interpreting fake time as live GPU cost.</summary>
internal sealed class ManualUploadTime : TimeProvider
{
    internal long Milliseconds;
    internal int Reads;
    public override long TimestampFrequency => 1000;
    /// <summary>Advances only when the test changes its clock, recording whether unused budgets queried time.</summary>
    public override long GetTimestamp() { Reads++; return Milliseconds; }
}
