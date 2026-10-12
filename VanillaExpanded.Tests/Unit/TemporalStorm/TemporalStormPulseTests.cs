using VanillaExpanded.TemporalStorm;

namespace VanillaExpanded.Tests.Unit.TemporalStorm;

/// <summary>Checks real-time warning animation without graphics or calendar dependencies.</summary>
public sealed class TemporalStormPulseTests
{
    #region Public API
    /// <summary>Both ends of the warning window and out-of-range values produce bounded pulse rates.</summary>
    [Theory]
    [InlineData(.02, 1)]
    [InlineData(.5, 1)]
    [InlineData(0, 1d / 6)]
    [InlineData(-1, 1d / 6)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void WarningWindowControlsFrequency(double remaining, double halfCycle)
    {
        var pulse = new TemporalStormPulse();
        pulse.SetRemainingDays(remaining);
        Assert.Equal(1, pulse.Advance(0), 5);
        Assert.Equal(.35f, pulse.Advance((float)halfCycle), 5);
        Assert.Equal(1, pulse.Advance((float)halfCycle), 5);
    }

    /// <summary>Urgency changes preserve the current phase and subsequently advance at the new rate.</summary>
    [Fact]
    public void RateUpdatesDoNotRestartPhase()
    {
        var pulse = new TemporalStormPulse();
        pulse.SetRemainingDays(.02);
        Assert.Equal(.675f, pulse.Advance(.5f), 5);
        pulse.SetRemainingDays(0);
        Assert.Equal(.675f, pulse.Advance(0), 5);
        Assert.Equal(.35f, pulse.Advance(1f / 12), 5);
        pulse.Reset();
        Assert.Equal(1, pulse.Advance(0), 5);
        Assert.Equal(.675f, pulse.Advance(.5f), 5);
    }

    /// <summary>Invalid frame times cannot corrupt phase and long-running output remains within the opacity range.</summary>
    [Fact]
    public void InvalidDeltaTimesLeaveAnimationUnchanged()
    {
        var pulse = new TemporalStormPulse();
        float initial = pulse.Advance(.3f);
        foreach (float delta in new[] { 0f, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Assert.Equal(initial, pulse.Advance(delta));
        pulse.SetRemainingDays(.01);
        for (int frame = 0; frame < 1000; frame++) Assert.InRange(pulse.Advance(.016f), .35f, 1f);
    }
    #endregion
}
