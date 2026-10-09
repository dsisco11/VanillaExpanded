using System.Numerics;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators;

/// <summary>Checks that rendering metadata follows the winning registration without disturbing provider sampling.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorEffectSelectionTests
{
    #region Public API
    #region Selection and Sampling
    /// <summary>An applicable zero-fill provider retains its priority and effect through cached queries.</summary>
    [Fact]
    public void CachedZeroFillSelection_KeepsWinningEffectAndPriority()
    {
        long now = 0;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        var slot = CreateSlot();
        var winner = new MutableProvider { Fill = 0 };
        var fallback = new MutableProvider { Fill = 1 };
        var absent = new MutableProvider { Applicable = false };
        var effect = CreateEffect();
        system.Register(fallback, priority: -10, effect: CreateEffect("test:fallback"));
        system.Register(winner, effect: effect);
        system.Register(absent, priority: 10, effect: CreateEffect("test:absent"));

        Assert.True(system.TryGetRenderSelection(slot, out var first));
        // Presentation changes remain hidden until expiry, including an applicable invisible fill.
        winner.Fill = 1;
        now = 999;
        Assert.True(system.TryGetRenderSelection(slot, out var cached));

        Assert.Equal(0, cached.Indicator.Fill);
        Assert.Equal(winner.Color, cached.Indicator.Color);
        Assert.Same(effect, cached.Effect);
        Assert.Equal(first, cached);
        Assert.Equal(1, winner.Calls);
        Assert.Equal(1, absent.Calls);
        Assert.Equal(0, fallback.Calls);
    }

    /// <summary>Equal priority chooses the first registration even when one provider object is registered twice.</summary>
    [Fact]
    public void EqualPriority_KeepsRegistrationEffectInsteadOfProviderIdentity()
    {
        var system = new ItemSlotIndicatorSystem();
        var provider = new MutableProvider();
        var firstEffect = CreateEffect("test:first");
        // A provider object's identity does not replace either registration's metadata ownership.
        system.Register(provider, effect: firstEffect);
        system.Register(provider, effect: CreateEffect("test:second"));

        Assert.True(system.TryGetRenderSelection(CreateSlot(), out var selection));

        Assert.Same(firstEffect, selection.Effect);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>A cached absent result can fall through to an ordinary registration without leaking its effect.</summary>
    [Fact]
    public void InapplicableEffectProvider_DoesNotThemePlainFallback()
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var slot = CreateSlot();
        var absent = new MutableProvider { Applicable = false };
        var plain = new MutableProvider();
        system.Register(plain);
        system.Register(absent, priority: 10, effect: CreateEffect());

        Assert.True(system.TryGetRenderSelection(slot, out var first));
        Assert.True(system.TryGetRenderSelection(slot, out var cached));

        // A cached miss carries no rendering metadata into the successful plain registration.
        Assert.Null(cached.Effect);
        Assert.Equal(new ItemSlotIndicator(plain.Fill, plain.Color), cached.Indicator);
        Assert.Equal(first, cached);
        Assert.Equal(1, absent.Calls);
        Assert.Equal(1, plain.Calls);
    }

    /// <summary>Configuration invalidation and adaptive polling update data while retaining the registered effect.</summary>
    [Fact]
    public void ContextAndAdaptiveSampling_KeepEffectOutsideSampleState()
    {
        long now = 0;
        int context = 0;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        var provider = new MutableProvider();
        var effect = CreateEffect();
        var slot = CreateSlot();
        system.Register(provider, contextKey: () => context, adaptiveSampling: new AdaptiveSamplingOptions(), effect: effect);
        Assert.True(system.TryGetRenderSelection(slot, out _));
        provider.Fill = 0.6f;
        context++;
        now = 10;
        Assert.True(system.TryGetRenderSelection(slot, out var refreshed));
        Assert.Equal(0.6f, refreshed.Indicator.Fill);
        Assert.Same(effect, refreshed.Effect);
        // Configuration starts a fresh idle context; the following sampled fill change activates fast polling.
        provider.Fill = 0.7f;
        now = 1010;
        Assert.True(system.TryGetRenderSelection(slot, out _));
        provider.Fill = 0.8f;
        now = 1109;
        Assert.True(system.TryGetRenderSelection(slot, out var cached));
        Assert.Equal(0.7f, cached.Indicator.Fill);
        now = 1110;
        Assert.True(system.TryGetRenderSelection(slot, out var active));

        Assert.Equal(0.8f, active.Indicator.Fill);
        Assert.Same(effect, active.Effect);
        Assert.Equal(4, provider.Calls);
    }
    #endregion

    #region Registration and Lifecycle
    /// <summary>A complete effect identity can be registered again using an equal immutable value.</summary>
    [Fact]
    public void IdenticalDefinitions_AreAcceptedAcrossRegistrations()
    {
        var system = new ItemSlotIndicatorSystem();
        system.Register(new MutableProvider { Applicable = false }, effect: CreateEffect());
        var equal = CreateEffect();
        system.Register(new MutableProvider(), effect: equal);

        Assert.True(system.TryGetRenderSelection(CreateSlot(), out var selection));
        Assert.Same(equal, selection.Effect);
    }

    /// <summary>Conflicting identity is rejected without inserting a higher-priority provider or changing cached data.</summary>
    [Theory]
    [InlineData("shader")]
    [InlineData("domain")]
    [InlineData("geometry")]
    [InlineData("parameters")]
    [InlineData("motion")]
    public void ConflictingEffectId_LeavesRegistryAndSamplesUnchanged(string change)
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var slot = CreateSlot();
        var provider = new MutableProvider();
        var rejected = new MutableProvider();
        var effect = CreateEffect();
        system.Register(provider, effect: effect);
        Assert.True(system.TryGetRenderSelection(slot, out var first));
        // Reject a higher-priority conflicting definition while the original sample is still cached.
        var conflict = new ItemSlotIndicatorEffectDefinition(effect.Id,
            change == "domain" ? "other" : effect.ShaderAssetDomain,
            change == "shader" ? "vanillaexpanded_itemslot_other" : effect.ShaderName,
            segmentCount: change == "geometry" ? 32 : effect.SegmentCount,
            parameters: change == "parameters" ? Vector4.One : effect.Parameters,
            needsCameraMotion: change == "motion");

        Assert.Throws<ArgumentException>(() => system.Register(rejected, priority: 10, effect: conflict));
        Assert.True(system.TryGetRenderSelection(slot, out var unchanged));

        Assert.Equal(first, unchanged);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, rejected.Calls);
    }

    /// <summary>Distinct appearances may share one program name only when its asset domain and ABI agree.</summary>
    [Fact]
    public void ShaderNameCollision_IsRejectedButParameterVariantsAreAccepted()
    {
        var system = new ItemSlotIndicatorSystem();
        var provider = new MutableProvider { Applicable = false };
        var effect = CreateEffect();
        system.Register(provider, effect: effect);
        var collision = new ItemSlotIndicatorEffectDefinition("test:other", "other", effect.ShaderName);
        Assert.Throws<ArgumentException>(() => system.Register(new MutableProvider(), priority: 10, effect: collision));
        // Shared program identity permits independent parameters, geometry, and motion requirements.
        var variant = new ItemSlotIndicatorEffectDefinition("test:variant", effect.ShaderAssetDomain,
            effect.ShaderName, segmentCount: 32, parameters: Vector4.One, needsCameraMotion: true);
        system.Register(new MutableProvider(), effect: variant);

        Assert.True(system.TryGetRenderSelection(CreateSlot(), out var selection));
        Assert.Same(variant, selection.Effect);
    }

    /// <summary>Clear discards old identity constraints and samples before a replacement registration.</summary>
    [Fact]
    public void Clear_RemovesEffectsAndTheirCachedSamples()
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var provider = new MutableProvider();
        var slot = CreateSlot();
        system.Register(provider, effect: CreateEffect());
        Assert.True(system.TryGetRenderSelection(slot, out _));
        system.Clear();
        Assert.False(system.TryGetRenderSelection(slot, out var cleared));
        Assert.Equal(default, cleared);
        // Time stays fixed so the next observed fill proves old samples were discarded by Clear.
        var replacement = new ItemSlotIndicatorEffectDefinition("test:waves", "other", "vanillaexpanded_itemslot_test");
        provider.Fill = 0.9f;
        system.Register(provider, effect: replacement);

        Assert.True(system.TryGetRenderSelection(slot, out var selection));
        Assert.Same(replacement, selection.Effect);
        Assert.Equal(0.9f, selection.Indicator.Fill);
        Assert.Equal(2, provider.Calls);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates one stable stack identity without a GUI or graphics context.</summary>
    private static ItemSlot CreateSlot() => new(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(1)) };

    /// <summary>Creates an effect description without loading its deliberately absent shader assets.</summary>
    private static ItemSlotIndicatorEffectDefinition CreateEffect(string id = "test:waves") =>
        new(id, "vanillaexpanded", "vanillaexpanded_itemslot_test");

    /// <summary>Supplies mutable presentation data and records authoritative provider samples.</summary>
    private sealed class MutableProvider : IItemSlotIndicatorProvider
    {
        internal int Calls;
        internal float Fill = 0.5f;
        internal Vector4 Color = new(0.2f, 0.4f, 0.8f, 0.5f);
        internal bool Applicable = true;

        #region Public API
        /// <summary>Counts one sample and returns the current provider-owned fill and color.</summary>
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
