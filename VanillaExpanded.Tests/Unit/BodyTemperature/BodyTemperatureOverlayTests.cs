using System.Drawing;
using Moq;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using VanillaExpanded.Tests.Unit.BowAmmunition;

namespace VanillaExpanded.Tests.Unit.BodyTemperature;

/// <summary>Verifies native freezing intensity, synchronized sampling, and cached warning lifetime.</summary>
[Collection("HudOverlayGeometry")]
public sealed class BodyTemperatureOverlayTests : IDisposable
{
    private readonly Vintagestory.API.Config.ITranslationService? english = Vintagestory.API.Config.Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    #region Fixture lifetime
    /// <summary>Installs deterministic localized labels without initializing the native language loader.</summary>
    public BodyTemperatureOverlayTests()
    {
        var service = new Mock<Vintagestory.API.Config.ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] arguments) => arguments.Length > 0 ? arguments[0] + "°C" : key);
        Vintagestory.API.Config.Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores borrowed global language state after each test.</summary>
    public void Dispose()
    {
        if (english == null) Vintagestory.API.Config.Lang.AvailableLanguages.Remove("en");
        else Vintagestory.API.Config.Lang.AvailableLanguages["en"] = english;
    }

    #endregion
    #region Sampling and presentation
    /// <summary>Native freezing strength determines cold visibility and clamps corrupt or out-of-range values.</summary>
    [Theory]
    [InlineData(32, 0, false, 0)]
    [InlineData(32, .25f, true, .25f)]
    [InlineData(37, .25f, true, .25f)]
    [InlineData(45, .25f, true, .25f)]
    [InlineData(32, 2, true, 1)]
    [InlineData(32, -1, false, 0)]
    [InlineData(32, float.NaN, false, 0)]
    [InlineData(32, float.PositiveInfinity, false, 0)]
    [InlineData(float.NaN, 1, false, 0)]
    [InlineData(float.PositiveInfinity, 1, false, 0)]
    public void ClassificationUsesNativeStrength(float current, float strength, bool visible, float normalized)
    {
        var sample = BodyTemperatureSample.FromTemperature(current, 37, strength);
        Assert.Equal(visible, sample != null);
        if (sample != null)
        {
            Assert.Equal(normalized, sample.FreezingStrength);
            Assert.Equal(normalized > .5f, sample.Dangerous);
        }
    }

    /// <summary>Sampling preserves prepared data until synchronized changes, recovery, death or disable.</summary>
    [Fact]
    public void SamplesSynchronizedStateAndCachesPreparedText()
    {
        var fixture = CreateFixture();
        using var presentation = new BodyTemperatureThermometerPresentation(measure: (_, _) => new(90, 18), loadBackdrop: _ => null);
        using var overlay = new BodyTemperatureOverlay(presentation);
        overlay.BeginSession(fixture.Api.Object);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh()); Assert.True(overlay.Measure().IsEmpty);
        SetTemperature(fixture, 34); overlay.Refresh();
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.Prepare(context); Assert.False(overlay.Measure().IsEmpty);
        string before = presentation.Reading;
        SetTemperature(fixture, 34.01f); overlay.Refresh(); overlay.Prepare(context);
        Assert.Equal(before, presentation.Reading);
        SetTemperature(fixture, 32); Assert.False(overlay.Sample!.Dangerous);
        overlay.Refresh(); Assert.True(overlay.Sample!.Dangerous);
        overlay.Prepare(context); Assert.NotEqual(before, presentation.Reading);
        SetTemperature(fixture, 35.25f); overlay.Refresh(); Assert.Null(overlay.Sample); Assert.True(overlay.Measure().IsEmpty);
        SetTemperature(fixture, 32); overlay.Refresh(); overlay.SetEnabled(false);
        Assert.Null(overlay.Sample); Assert.False(overlay.IsApplicable());
        overlay.SetEnabled(true); overlay.Refresh(); fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable()); Assert.Null(overlay.Sample);
        overlay.EndSession(); overlay.EndSession();
    }

    /// <summary>Ordinary warming to the native simulation ceiling clears rather than showing a red warning.</summary>
    [Fact]
    public void NativeWarmthCeilingDoesNotIndicateOverheating()
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        SetTemperature(fixture, 32);
        overlay.Refresh();
        Assert.NotNull(overlay.Sample);
        SetTemperature(fixture, 45, 0);
        overlay.Refresh();
        Assert.Null(overlay.Sample);
        Assert.Equal(SizeF.Empty, overlay.Measure());
    }

    /// <summary>Samples native timestamps while healthy and clears trend history across entity replacement.</summary>
    [Fact]
    public void TrendSurvivesRepeatedPollsAndResetsForReplacementEntity()
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        SetTemperature(fixture, 37);
        fixture.Entity.WatchedAttributes.GetTreeAttribute("bodyTemp").SetDouble("bodyTempUpdateTotalHours", 10);
        overlay.Refresh();
        SetTemperature(fixture, 34);
        fixture.Entity.WatchedAttributes.GetTreeAttribute("bodyTemp").SetDouble("bodyTempUpdateTotalHours", 11);
        overlay.Refresh();
        Assert.Equal(-2, overlay.Sample!.Trend);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        var replacement = CreateFixture();
        SetTemperature(replacement, 34);
        replacement.Entity.WatchedAttributes.GetTreeAttribute("bodyTemp").SetDouble("bodyTempUpdateTotalHours", 12);
        fixture.Entity = replacement.Entity;
        overlay.Refresh();
        Assert.Equal(0, overlay.Sample!.Trend);
        fixture.Entity.WatchedAttributes.RemoveAttribute("bodyTemp");
        overlay.Refresh();
        Assert.Null(overlay.Sample);
    }

    /// <summary>Missing behavior or synchronized tree never fabricates a dangerous zero-temperature reading.</summary>
    [Fact]
    public void MissingDataRemainsHiddenAndNewEntityIsSampled()
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        fixture.Entity.WatchedAttributes.RemoveAttribute("bodyTemp");
        overlay.Refresh();
        Assert.Null(overlay.Sample);
        SetTemperature(fixture, 32);
        overlay.Refresh();
        Assert.True(overlay.Sample!.Dangerous);
        fixture.Entity.Properties.Client.Behaviors.Clear();
        overlay.Refresh();
        Assert.Null(overlay.Sample);
        var second = CreateFixture();
        SetTemperature(second, 34);
        fixture.Entity = second.Entity;
        overlay.Refresh();
        Assert.Equal(34, overlay.Sample!.Celsius);
        Assert.False(overlay.Sample.Dangerous);
        overlay.EndSession();
        Assert.Null(overlay.Sample);
        overlay.BeginSession(second.Api.Object);
        overlay.Refresh();
        Assert.Equal(34, overlay.Sample!.Celsius);
    }
    /// <summary>Game modes exempt from native cold simulation clear an existing warning immediately.</summary>
    [Theory]
    [InlineData(EnumGameMode.Creative)]
    [InlineData(EnumGameMode.Spectator)]
    public void ExemptGameModesClearPreparedWarning(EnumGameMode mode)
    {
        var fixture = CreateFixture();

        using var overlay = new BodyTemperatureOverlay(new BodyTemperatureThermometerPresentation(measure: (_, _) => new(90, 18), loadBackdrop: _ => null));
        overlay.BeginSession(fixture.Api.Object);
        SetTemperature(fixture, 32);
        overlay.Refresh();
        overlay.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"));
        Assert.NotNull(overlay.Sample);
        var worldData = new Mock<IWorldPlayerData>();
        worldData.SetupGet(value => value.CurrentGameMode).Returns(mode);
        fixture.Player.SetupGet(value => value.WorldData).Returns(worldData.Object);
        Assert.False(overlay.IsApplicable());
        Assert.Null(overlay.Sample);

        Assert.Equal(SizeF.Empty, overlay.Measure());
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
    }

    /// <summary>Re-enabling after recovery invalidates cached host geometry even when the reading remains hidden.</summary>
    [Fact]
    public void HealthyReenableInvalidatesPreviousWarningMeasurement()
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        SetTemperature(fixture, 32);
        overlay.Refresh();
        overlay.SetEnabled(false);
        SetTemperature(fixture, 37);
        overlay.SetEnabled(true);
        Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        Assert.Null(overlay.Sample);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
    }

    /// <summary>New local entities use their own native cold strength and cannot inherit hot recovery margins.</summary>
    [Theory]
    [InlineData(34, 35.1f)]
    [InlineData(97, 86)]
    public void ReplacementEntityDoesNotInheritRecoveryMargin(float original, float replacement)
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object); SetTemperature(fixture, original); overlay.Refresh();
        Assert.NotNull(overlay.Sample);
        var second = CreateFixture(); SetTemperature(second, replacement); fixture.Entity = second.Entity;
        overlay.Refresh(); Assert.Null(overlay.Sample);
    }

    /// <summary>The optional reading changes canonical measurement without resampling temperature.</summary>
    [Fact]
    public void ReadingPreferenceChangesMeasurementWithoutRebuildingArtwork()
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay(new BodyTemperatureThermometerPresentation(measure: (_, _) => new(28, 18), loadBackdrop: _ => null));
        overlay.BeginSession(fixture.Api.Object); SetTemperature(fixture, 34); overlay.Refresh();
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"); overlay.Prepare(context);
        var withReading = overlay.Measure();
        overlay.SetShowReading(false); Assert.NotEqual(HudOverlayChange.None, overlay.Refresh()); overlay.Prepare(context);
        Assert.True(overlay.Measure().Height < withReading.Height);
        overlay.SetShowReading(true); overlay.Refresh(); overlay.Prepare(context);
        Assert.Equal(withReading, overlay.Measure());
    }

    /// <summary>Native strength changes update the warning, while zero clears cold immediately.</summary>
    [Fact]
    public void NativeStrengthChangesReuseResourcesAndClearColdAtZero()
    {
        var fixture = CreateFixture();
        using var overlay = new BodyTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        SetTemperature(fixture, 32, 0); overlay.Refresh(); Assert.Null(overlay.Sample);
        fixture.Entity.WatchedAttributes.RemoveAttribute("freezingEffectStrength");
        overlay.Refresh(); Assert.Null(overlay.Sample);
        SetTemperature(fixture, 37, .25f); overlay.Refresh(); Assert.NotNull(overlay.Sample);
        SetTemperature(fixture, 37, .75f); Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(37, overlay.Sample!.DisplayCelsius); Assert.True(overlay.Sample.Dangerous);
        SetTemperature(fixture, 32, 0); overlay.Refresh(); Assert.Null(overlay.Sample); Assert.True(overlay.Measure().IsEmpty);
        SetTemperature(fixture, 97, 0); overlay.Refresh(); Assert.Equal(BodyTemperatureRisk.Hot, overlay.Sample!.Risk);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Attaches the native temperature behavior without running the game's server simulation.</summary>
    private static BowAmmunitionSamplerTests.Fixture CreateFixture()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Api.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        fixture.World.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        fixture.Entity.Api = fixture.Api.Object;
        typeof(Vintagestory.API.Common.Entities.Entity).GetProperty("Properties")!.SetValue(fixture.Entity,
            new EntityProperties { Client = new EntityClientProperties([], new Dictionary<string, JsonObject>()) });
        fixture.Entity.Properties.Client.Behaviors.Add(new EntityBehaviorBodyTemperature(fixture.Entity) { NormalBodyTemperature = 37 });
        var worldData = new Mock<IWorldPlayerData>();
        worldData.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Survival);
        fixture.Player.SetupGet(value => value.WorldData).Returns(worldData.Object);
        fixture.Entity.Alive = true;
        Assert.Same(fixture.Entity.Properties.Client, fixture.Entity.SidedProperties);
        Assert.NotNull(fixture.Entity.GetBehavior<EntityBehaviorBodyTemperature>());
        SetTemperature(fixture, 37);
        return fixture;
    }
    /// <summary>Writes the same synchronized attribute path used by the native temperature behavior.</summary>
    private static void SetTemperature(BowAmmunitionSamplerTests.Fixture fixture, float value, float? freezingStrength = null)
    {
        var tree = fixture.Entity.WatchedAttributes.GetTreeAttribute("bodyTemp") ?? new TreeAttribute();
        tree.SetFloat("bodytemp", value);
        fixture.Entity.WatchedAttributes.SetAttribute("bodyTemp", tree);
        // Representative native server intensity belongs only to this fixture, never to overlay policy.
        fixture.Entity.WatchedAttributes.SetFloat("freezingEffectStrength", freezingStrength ?? Math.Clamp((35 - value) / 4, 0, 1));
    }
    #endregion
}
