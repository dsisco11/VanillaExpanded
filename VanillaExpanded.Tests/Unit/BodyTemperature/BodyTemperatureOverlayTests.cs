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
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] arguments) => key);
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

    /// <summary>Sampling changes only when displayed state changes and releases warnings on recovery, death, or disable.</summary>
    [Fact]
    public void SamplesSynchronizedStateAndCachesPreparedText()
    {
        var fixture = CreateFixture();
        int builds = 0;
        var textures = new List<LoadedTexture>();
        var presentation = new HudOverlayIconTextPresentation((context, _) =>
        {
            builds++;
            var texture = new LoadedTexture(context.Api) { Width = 90, Height = 18 };
            textures.Add(texture);
            return texture;
        });
        using var overlay = new BodyTemperatureOverlay(presentation, CreateIcons);
        overlay.BeginSession(fixture.Api.Object);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        SetTemperature(fixture, 34);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.Prepare(preparation);
        Assert.Equal(1, builds);
        Assert.Equal(new SizeF(107, 24), overlay.Measure());
        SetTemperature(fixture, 34.01f);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        overlay.Prepare(preparation);
        Assert.Equal(1, builds);
        // Drawing uses the snapshot even if synchronized gameplay changes before the next refresh.
        SetTemperature(fixture, 32);
        var bounds = ElementBounds.Fixed(0, 0, 90, 18).WithEmptyParent();
        bounds.CalcWorldBounds();
        overlay.Draw(new Mock<IRenderAPI>().Object, bounds, new RectangleF(0, 0, 90, 18), .1f);
        Assert.False(overlay.Sample!.Dangerous);
        overlay.Refresh();
        Assert.True(overlay.Sample!.Dangerous);
        overlay.Prepare(preparation);
        Assert.Equal(2, builds);
        Assert.True(textures[0].Disposed);
        SetTemperature(fixture, 35.25f);
        overlay.Refresh();
        Assert.Null(overlay.Sample);
        Assert.True(textures[1].Disposed);
        Assert.Equal(SizeF.Empty, overlay.Measure());
        SetTemperature(fixture, 32);
        overlay.Refresh();
        overlay.SetEnabled(false);
        Assert.Null(overlay.Sample);
        Assert.False(overlay.IsApplicable());
        overlay.SetEnabled(true);
        overlay.Refresh();
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
        Assert.Null(overlay.Sample);
        overlay.EndSession();
        overlay.EndSession();
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
        var texture = new LoadedTexture(fixture.Api.Object) { Width = 90, Height = 18 };
        using var overlay = new BodyTemperatureOverlay(new HudOverlayIconTextPresentation((_, _) => texture), CreateIcons);
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
        Assert.True(texture.Disposed);
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

    /// <summary>The optional reading releases text while preserving thermometer artwork and updates layout live.</summary>
    [Fact]
    public void ReadingPreferenceChangesMeasurementWithoutRebuildingArtwork()
    {
        var fixture = CreateFixture(); int artBuilds = 0, textBuilds = 0;
        var ownedText = new List<LoadedTexture>();
        var text = new HudOverlayIconTextPresentation((context, _) =>
        {
            textBuilds++; var texture = new LoadedTexture(context.Api) { Width = 28, Height = 18 };
            ownedText.Add(texture); return texture;
        });
        using var overlay = new BodyTemperatureOverlay(text, context => { artBuilds++; return CreateIcons(context); });
        overlay.BeginSession(fixture.Api.Object); SetTemperature(fixture, 34); overlay.Refresh();
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"); overlay.Prepare(context);
        Assert.Equal(new SizeF(45, 24), overlay.Measure());
        var bounds = ElementBounds.Fixed(100, 200, 45, 24).WithEmptyParent(); bounds.CalcWorldBounds();
        var renderer = new Mock<IRenderAPI>(); overlay.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), .1f);
        var calls = renderer.Invocations.Where(call => call.Method.Name is "Render2DTexturePremultipliedAlpha" or "RenderTexture").ToArray();
        Assert.Equal(3, calls.Length); Assert.Equal(bounds.renderX + 17, (double)calls[2].Arguments[1]);
        Assert.Equal(new[] { "PushScissor", "RenderTexture", "PopScissor", "RenderTexture", "Render2DTexturePremultipliedAlpha" }, renderer.Invocations.Select(call => call.Method.Name));
        overlay.SetShowReading(false); Assert.NotEqual(HudOverlayChange.None, overlay.Refresh()); overlay.Prepare(context);
        Assert.Equal(new SizeF(14, 24), overlay.Measure()); Assert.True(ownedText[0].Disposed);
        Assert.Equal(1, artBuilds); Assert.Equal(1, textBuilds);
        overlay.SetShowReading(true); overlay.Refresh(); overlay.Prepare(context);
        Assert.Equal(1, artBuilds); Assert.Equal(2, textBuilds); Assert.Equal(new SizeF(45, 24), overlay.Measure());
    }

    /// <summary>Strength-only changes update tint without rebuilding text or art and zero clears native cold immediately.</summary>
    [Fact]
    public void NativeStrengthControlsWarningAndCachedTint()
    {
        var fixture = CreateFixture(); int textBuilds = 0, iconBuilds = 0;
        var layers = new List<LoadedTexture>();
        using var overlay = new BodyTemperatureOverlay(new HudOverlayIconTextPresentation((context, _) =>
        {
            textBuilds++; return new LoadedTexture(context.Api) { Width = 28, Height = 18 };
        }), context => { iconBuilds++; var result = CreateIcons(context); layers.AddRange(result); return result; });
        overlay.BeginSession(fixture.Api.Object);
        SetTemperature(fixture, 32, 0); overlay.Refresh(); Assert.Null(overlay.Sample);
        fixture.Entity.WatchedAttributes.RemoveAttribute("freezingEffectStrength");
        overlay.Refresh(); Assert.Null(overlay.Sample);
        SetTemperature(fixture, 37, .25f); overlay.Refresh(); Assert.NotNull(overlay.Sample);
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"); overlay.Prepare(context);
        var bounds = ElementBounds.Fixed(0, 0, 45, 24).WithEmptyParent(); bounds.CalcWorldBounds();
        var renderer = new Mock<IRenderAPI>();
        overlay.Draw(renderer.Object, bounds, new RectangleF(0, 0, 100, 100), .1f);
        var firstTint = (Vintagestory.API.MathTools.Vec4f)renderer.Invocations.First(call => call.Method.Name == "RenderTexture").Arguments[6];
        Assert.Equal(.5125f, firstTint.R, 5); Assert.Equal(.7875f, firstTint.G, 5);
        SetTemperature(fixture, 37, .75f);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh()); overlay.Prepare(context);
        Assert.Equal(1, textBuilds); Assert.Equal(1, iconBuilds);
        renderer.Invocations.Clear(); overlay.Draw(renderer.Object, bounds, new RectangleF(0, 0, 100, 100), .1f);
        var secondTint = (Vintagestory.API.MathTools.Vec4f)renderer.Invocations.First(call => call.Method.Name == "RenderTexture").Arguments[6];
        Assert.Equal(.3375f, secondTint.R, 5); Assert.Equal(.6625f, secondTint.G, 5);
        Assert.Equal(37, overlay.Sample!.DisplayCelsius);
        SetTemperature(fixture, 32, 0); overlay.Refresh(); Assert.Null(overlay.Sample);
        Assert.Equal(2, layers.Count); Assert.Equal(SizeF.Empty, overlay.Measure());
        SetTemperature(fixture, 97, 0); overlay.Refresh(); Assert.Equal(BodyTemperatureRisk.Hot, overlay.Sample!.Risk);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Supplies owned texture layers without opening a graphics context.</summary>
    private static LoadedTexture[] CreateIcons(HudOverlayPreparationContext context) =>
        [new(context.Api) { TextureId = 1, IgnoreUndisposed = true }, new(context.Api) { TextureId = 2, IgnoreUndisposed = true }];
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
