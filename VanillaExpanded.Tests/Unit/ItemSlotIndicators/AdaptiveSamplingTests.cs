using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators;

/// <summary>Checks adaptive scheduling deterministically through observable provider calls.</summary>
[Trait("Category", "Unit")]
public sealed class AdaptiveSamplingTests
{
    #region Public API
    #region Scheduling
    /// <summary>Invisible output changes do not activate an absent provider or accelerate a fallback registration.</summary>
    [Fact]
    public void Registrations_KeepIndependentActivity()
    {
        long now = 0;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        var absent = new MutableProvider { Applicable = false };
        var visible = new MutableProvider();
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(1)) };
        system.Register(absent, priority: 10, adaptiveSampling: new AdaptiveSamplingOptions());
        system.Register(visible, adaptiveSampling: new AdaptiveSamplingOptions());
        system.TryGetIndicator(slot, out _);
        absent.Fill = 1;
        visible.Fill = 0.5f;
        now = 1000;
        system.TryGetIndicator(slot, out _);
        now = 1100;
        system.TryGetIndicator(slot, out _);
        Assert.Equal(2, absent.Calls);
        Assert.Equal(3, visible.Calls);
    }

    /// <summary>Meaningful changes activate faster polling, sustained activity extends it, and quiet returns to idle.</summary>
    [Fact]
    public void Activity_ExtendsThenSettles()
    {
        var f = new Fixture();
        f.Query(0, 1);
        f.Provider.Fill = 0.5f;
        f.Query(999, 1);
        f.Query(1000, 2);
        f.Query(1099, 2);
        f.Query(1100, 3);
        f.Provider.Fill = 0.7f;
        f.Query(1900, 4);
        f.Query(2000, 5);
        f.Query(2800, 6);
        f.Query(2900, 7);
        f.Query(3000, 7);
        f.Query(3899, 7);
        f.Query(3900, 8);
    }

    /// <summary>Exactly-threshold changes and cumulative slow drift do not activate faster sampling.</summary>
    [Theory]
    [InlineData(0.005f)]
    [InlineData(0.004f)]
    public void SmallConsecutiveChanges_StayIdle(float increment)
    {
        var f = new Fixture();
        f.Query(0, 1);
        f.Provider.Fill = increment;
        f.Query(1000, 2);
        f.Query(1100, 2);
        f.Provider.Fill = increment * 2;
        f.Query(2000, 3);
        f.Query(2100, 3);
    }

    /// <summary>Each RGBA channel can activate polling independently of fill.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ColorChange_Activates(int channel)
    {
        var f = new Fixture();
        f.Query(0, 1);
        var color = Vector4.Zero;
        color[channel] = 0.01f;
        f.Provider.Color = color;
        f.Query(1000, 2);
        f.Query(1100, 2);
        color[channel] = 0.03f;
        f.Provider.Color = color;
        f.Query(2000, 3);
        f.Query(2100, 4);
    }

    /// <summary>Gaining and losing applicability both activate sampling, while absent output noise is ignored.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplicabilityChange_Activates(bool initial)
    {
        var f = new Fixture();
        f.Provider.Applicable = initial;
        f.Query(0, 1);
        f.Provider.Applicable = !initial;
        f.Query(1000, 2);
        f.Query(1100, 3);
    }

    /// <summary>Missed queries cause one sample and schedule from the present time without catch-up.</summary>
    [Fact]
    public void LongGap_DoesNotCatchUp()
    {
        var f = new Fixture();
        f.Query(0, 1);
        f.Provider.Fill = 0.5f;
        f.Query(1000, 2);
        f.Query(100000, 3);
        f.Query(100001, 3);
        f.Query(100999, 3);
        f.Query(101000, 4);
    }

    /// <summary>Context changes and clearing a registration reset active polling to the idle interval.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextOrLifecycleReset_ReturnsToIdle(bool clear)
    {
        var f = new Fixture();
        f.Query(0, 1);
        f.Provider.Fill = 0.5f;
        f.Query(1000, 2);
        if (clear)
        {
            f.System.Clear();
            f.System.Register(f.Provider, adaptiveSampling: new AdaptiveSamplingOptions());
        }
        else f.Slot.Itemstack!.SetFrom(new ItemStack(MockItem.CreateNonLightSource(2)));
        f.Query(1050, 3);
        f.Query(1150, 3);
        f.Query(2049, 3);
        f.Query(2050, 4);
    }

    /// <summary>Activity on one stack does not accelerate another stack of the same provider.</summary>
    [Fact]
    public void Stacks_KeepIndependentActivity()
    {
        var f = new Fixture();
        var other = new ItemSlot(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(2)) };
        f.Query(0, 1);
        f.System.TryGetIndicator(other, out _);
        f.Provider.Fill = 0.5f;
        f.Query(1000, 3);
        f.Provider.Fill = 0;
        f.System.TryGetIndicator(other, out _);
        f.Query(1100, 5);
        f.System.TryGetIndicator(other, out _);
        Assert.Equal(5, f.Provider.Calls);
    }

    /// <summary>Explicit zero is immediate even with adaptive options, and omitted options remain fixed-rate.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptInAndZeroInterval_AreRespected(bool immediate)
    {
        long now = 0;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        var provider = new MutableProvider();
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(1)) };
        system.Register(provider, refreshIntervalMilliseconds: immediate ? 0 : 1000,
            adaptiveSampling: immediate ? new AdaptiveSamplingOptions() : null);
        system.TryGetIndicator(slot, out _);
        provider.Fill = 1;
        now = 1000;
        system.TryGetIndicator(slot, out _);
        now = 1100;
        system.TryGetIndicator(slot, out _);
        Assert.Equal(immediate ? 3 : 2, provider.Calls);
    }
    #endregion

    #region Validation
    /// <summary>Explicit construction supplies the standard policy rather than zero-initialized struct values.</summary>
    [Fact]
    public void ParameterlessConstruction_UsesStandardPolicy()
    {
        var options = new AdaptiveSamplingOptions();
        Assert.Equal(100, options.ActiveIntervalMilliseconds);
        Assert.Equal(1000, options.SettleMilliseconds);
        Assert.Equal(0.005f, options.FillChangeThreshold);
        Assert.Equal(0.01f, options.ColorChangeThreshold);
        Assert.Equal(new AdaptiveSamplingOptions(100, 1000, 0.005f, 0.01f), options);
    }

    /// <summary>Registration rejects zero-initialized options even when immediate sampling would bypass caching.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void DefaultStruct_IsRejectedByRegistration(long idleInterval)
    {
        AdaptiveSamplingOptions options = default;
        var system = new ItemSlotIndicatorSystem();
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Register(new MutableProvider(),
            refreshIntervalMilliseconds: idleInterval, adaptiveSampling: options));
        Assert.False(system.TryGetIndicator(new ItemSlot(null), out _));
    }

    /// <summary>Negative and nonfinite tolerances are rejected for either output component.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidThreshold_IsRejected(float threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveSamplingOptions(fillChangeThreshold: threshold));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveSamplingOptions(colorChangeThreshold: threshold));
    }

    /// <summary>Timing options must be positive, and active intervals must be shorter than nonzero idle intervals.</summary>
    [Fact]
    public void InvalidTiming_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveSamplingOptions(activeIntervalMilliseconds: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdaptiveSamplingOptions(settleMilliseconds: -1));
        var system = new ItemSlotIndicatorSystem();
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Register(new MutableProvider(), adaptiveSampling: new AdaptiveSamplingOptions(1000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => system.Register(new MutableProvider(), adaptiveSampling: new AdaptiveSamplingOptions(1001)));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Owns a deterministic clock and one adaptive registration.</summary>
    private sealed class Fixture
    {
        internal readonly ItemSlotIndicatorSystem System = new();
        internal readonly MutableProvider Provider = new();
        internal readonly ItemSlot Slot = new(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(1)) };
        private long now;

        #region Public API
        /// <summary>Registers a provider with the default adaptive settings.</summary>
        internal Fixture()
        {
            System.Clock = () => now;
            System.Register(Provider, adaptiveSampling: new AdaptiveSamplingOptions());
        }

        /// <summary>Advances the clock, queries, and checks the number of actual samples.</summary>
        internal void Query(long time, int calls)
        {
            now = time;
            System.TryGetIndicator(Slot, out _);
            Assert.Equal(calls, Provider.Calls);
        }
        #endregion
    }

    /// <summary>Supplies mutable output independently of invocation count.</summary>
    private sealed class MutableProvider : IItemSlotIndicatorProvider
    {
        internal int Calls;
        internal float Fill;
        internal Vector4 Color;
        internal bool Applicable = true;

        #region Public API
        /// <summary>Counts a sample and returns the current requested output.</summary>
        public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
        {
            Calls++;
            indicator = new ItemSlotIndicator(Fill, Color);
            return Applicable;
        }
        #endregion
    }
    #endregion
}
