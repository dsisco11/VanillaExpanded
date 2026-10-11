using VanillaExpanded.BodyTemperature;

namespace VanillaExpanded.Tests.Unit.BodyTemperature;

/// <summary>Verifies native sample timing, direction, severity, and trend lifetime without graphics.</summary>
public sealed class BodyTemperatureTrendTests
{
    #region Public API
    /// <summary>Rate uses elapsed simulation hours, preserves direction between polls, and settles on a stable new sample.</summary>
    [Theory]
    [InlineData(34.01f, 0)]
    [InlineData(34.25f, 1)]
    [InlineData(36, 2)]
    [InlineData(33.75f, -1)]
    [InlineData(32, -2)]
    public void ClassifiesRateAcrossNativeSamples(float next, int expected)
    {
        var trend = new BodyTemperatureTrend();
        Assert.Equal(0, trend.Update(34, 10, 0));
        Assert.Equal(expected, trend.Update(next, 11, 3000));
        Assert.Equal(expected, trend.Update(next, 11, 3250));
        Assert.Equal(0, trend.Update(next, 12, 6000));
    }

    /// <summary>Warmth above 37 is converted before measuring the trend.</summary>
    [Fact]
    public void WarmthUsesConvertedRate()
    {
        var trend = new BodyTemperatureTrend();
        trend.Update(41, 10, 0);
        Assert.Equal(1, trend.Update(45, 11, 3000));
    }

    /// <summary>Reset, clock reversal, invalid data, and long publication gaps never fabricate a trend.</summary>
    [Fact]
    public void DiscontinuitiesDiscardHistory()
    {
        var trend = new BodyTemperatureTrend();
        trend.Update(34, 10, 0);
        Assert.Equal(-2, trend.Update(32, 11, 3000));
        Assert.Equal(0, trend.Update(32, 11, 19000));
        Assert.Equal(2, trend.Update(34, 12, 20000));
        Assert.Equal(0, trend.Update(33, 9, 21000));
        Assert.Equal(0, trend.Update(float.NaN, 10, 22000));
        Assert.Equal(0, trend.Update(32, 11, 23000));
        trend.Reset();
        Assert.Equal(0, trend.Update(34, 12, 24000));
    }
    #endregion
}
