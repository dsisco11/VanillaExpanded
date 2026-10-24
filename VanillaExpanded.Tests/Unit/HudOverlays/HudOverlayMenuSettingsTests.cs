using System.Drawing;
using Newtonsoft.Json;
using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks optional feature-owned menu bindings against authoritative configuration and layout state.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayMenuSettingsTests : IDisposable
{
    private readonly string? oldLocale = Lang.CurrentLocale;
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    /// <summary>Provides native localization for system hotkey registration.</summary>
    public HudOverlayMenuSettingsTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = language.Object;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
    }
    /// <summary>Restores engine localization after the system integration checks.</summary>
    public void Dispose()
    {
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, oldLocale);
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }
    /// <summary>Every actual feature exposes its own enable flag and enum choices and saves before live notification.</summary>
    [Theory]
    [InlineData("ApproximateTime", "EnableApproximateTimeOverlay", "ApproximateTimeGaugeStyle")]
    [InlineData("EnvironmentalTemperature", "EnableEnvironmentalTemperatureOverlay", "EnvironmentalTemperatureGaugeStyle")]
    [InlineData("CalendarDate", "EnableCalendarDateOverlay", "CalendarDateGaugeStyle")]
    [InlineData("BodyTemperature", "EnableBodyTemperatureOverlay", "BodyTemperatureIndicatorStyle")]
    [InlineData("PlayerWetness", "EnablePlayerWetnessOverlay", "PlayerWetnessIndicatorStyle")]
    [InlineData("WeatherConditions", "EnableWeatherConditionsOverlay", "WeatherConditionsIndicatorStyle")]
    [InlineData("WindSpeed", "EnableWindSpeedOverlay", "WindSpeedIndicatorStyle")]
    [InlineData("TemporalStorm", "EnableTemporalStormOverlay", "TemporalStormIndicatorStyle")]
    [InlineData("RiftActivity", "EnableRiftActivityOverlay", "RiftActivityIndicatorStyle")]
    [InlineData("BowAmmunition", "EnableBowAmmunitionOverlay", "BowAmmunitionIndicatorStyle")]
    public void ActualFeatureBindingsUseAuthoritativeSettings(string featureName, string enableName, string styleName)
    {
        var config = VanillaExpandedModSystem.Config;
        var enabled = config.GetType().GetProperty(enableName)!;
        var style = config.GetType().GetProperty(styleName)!;
        var previousEnabled = enabled.GetValue(config); var previousStyle = style.GetValue(config);
        bool previousGlobal = config.EnableHudOverlays; VanillaExpandedConfig? persisted = null;
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock }; var loader = new Mock<IModLoader>();
        api.SetupGet(value => value.Event).Returns(Mock.Of<IClientEventAPI>());
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var hud = new HudOverlaySystem();
        var feature = (ModSystem)Activator.CreateInstance(typeof(HudOverlaySystem).Assembly.GetType($"VanillaExpanded.{featureName}.{featureName}System")!, true)!;
        int saves = 0; var observer = new ReloadObserver(() => Assert.True(saves > 0));
        loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
        loader.SetupGet(value => value.Systems).Returns(new ModSystem[] { observer, feature });
        api.Setup(value => value.StoreModConfig(config, Constants.ConfigFileName)).Callback(() => { saves++; persisted = JsonConvert.DeserializeObject<VanillaExpandedConfig>(JsonConvert.SerializeObject(config)); });
        try
        {
            hud.StartClientSide(api.Object); feature.StartClientSide(api.Object);
            HudOverlayMenuSettings? menu = null;
            hud.Registry.RunPass(registration => menu = registration.MenuSettings);
            Assert.NotNull(menu); Assert.NotNull(menu.Enabled); Assert.NotNull(menu.Style);
            // Direct edits must remain authoritative even without invoking a menu callback.
            enabled.SetValue(config, false); Assert.False(menu.Enabled.Value);
            enabled.SetValue(config, true); config.EnableHudOverlays = false; Assert.True(menu.Enabled.Value);
            menu.Enabled.Set(false); Assert.False((bool)enabled.GetValue(config)!);
            Assert.Equal(1, saves); Assert.Equal(1, observer.Reloads);
            Assert.False((bool)enabled.GetValue(persisted)!);
            menu.Enabled.Set(false); Assert.Equal(1, saves);
            foreach (var choice in menu.Style.Choices)
            {
                var external = menu.Style.Choices.First(value => !value.Equals(choice));
                style.SetValue(config, external); Assert.Equal(external, menu.Style.Value);
                menu.Style.Set(choice); Assert.Equal(choice, style.GetValue(config));
                Assert.Equal(choice, style.GetValue(persisted));
            }
            Assert.Equal(Enum.GetValues(style.PropertyType).Cast<Enum>(), menu.Style.Choices);
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.Style.Set(DayOfWeek.Monday));
            Assert.Throws<ArgumentOutOfRangeException>(() => menu.Style.Set((Enum)Enum.ToObject(style.PropertyType, int.MaxValue)));
        }
        finally
        {
            enabled.SetValue(config, previousEnabled); style.SetValue(config, previousStyle); config.EnableHudOverlays = previousGlobal;
            feature.Dispose(); hud.Dispose();
        }
    }

    /// <summary>Unsupported settings remain absent without altering existing registration callers.</summary>
    [Fact]
    public void MissingCapabilitiesRemainOptional()
    {
        using var fixture = new HudLayoutFixture();
        Assert.Null(fixture.Add("test:legacy").MenuSettings);
        var menu = new HudOverlayMenuSettings(); Assert.Null(menu.Enabled); Assert.Null(menu.Style);
        Assert.Contains(typeof(HudOverlayRegistration).GetConstructors(), constructor => constructor.GetParameters().Length == 8 && constructor.GetParameters().All(parameter => parameter.ParameterType != typeof(HudOverlayMenuSettings)));
    }

    /// <summary>Storage errors restore the authoritative value and do not emit a live reload.</summary>
    [Fact]
    public void FailedSaveRollsBackWithoutReload()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock }; var loader = new Mock<IModLoader>();
        var observer = new ReloadObserver(() => Assert.Fail("Failed persistence must not reload."));
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        loader.SetupGet(value => value.Systems).Returns(new ModSystem[] { observer });
        api.Setup(value => value.StoreModConfig(VanillaExpandedModSystem.Config, Constants.ConfigFileName)).Throws(new IOException());
        bool current = true;
        var setting = new HudOverlaySetting<bool>(() => current, value => current = value, () => HudMenuConfiguration.Commit(api.Object));
        Assert.Throws<IOException>(() => setting.Set(false)); Assert.True(current); Assert.Equal(0, observer.Reloads);
    }

    /// <summary>Lock applies to the effective owner while preserving origin, membership and restart state.</summary>
    [Theory]
    [InlineData(false, 21, 37)]
    [InlineData(true, 39, 52)]
    public void LockPreservesEffectiveContainer(bool grouped, float x, float y)
    {
        using var fixture = new HudLayoutFixture(); fixture.Add("test:a"); if (grouped) fixture.Add("test:b");
        var containers = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        var member = containers.Get("test", "test:a"); var origin = new PointF(x, y); member.ContainerOrigin = origin;
        member.SetLocked(false); Assert.False(member.Locked);
        member.SetLocked(true); Assert.True(member.Locked);
        Assert.Equal(origin, member.Position);
        var restored = new HudOverlayContainers(fixture.Api.Object, fixture.Registry);
        Assert.True(restored.Get("test", "test:a").Locked);
        Assert.Equal(origin, restored.Get("test", "test:a").Position);
        Assert.Equal(grouped ? new[] { "test:a", "test:b" } : new[] { "test:a" }, Assert.Single(restored.Resolved).Tree.Leaves());
        if (grouped) Assert.True(restored.Get("test", "test:b").Locked);
    }
    #endregion

    #region Private
    /// <summary>Observes the existing live-reload contract without allocating any rendering resources.</summary>
    private sealed class ReloadObserver(Action onReload) : ModSystem, ILiveConfigurable
    {
        public int Reloads { get; private set; }
        /// <summary>Records notifications after checking the caller-provided persistence ordering.</summary>
        public void OnConfigReloaded(ICoreAPI api) { onReload(); Reloads++; }
    }
    #endregion
}



