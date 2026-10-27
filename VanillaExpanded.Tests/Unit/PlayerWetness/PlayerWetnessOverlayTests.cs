using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Config;
using VanillaExpanded.PlayerWetness;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.PlayerWetness;

/// <summary>Checks native wetness sampling, fill geometry, and cached artwork lifetime.</summary>
[Collection("HudOverlayGeometry")]
public sealed class PlayerWetnessOverlayTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Installs a deterministic percentage translation for cached text tests.</summary>
    public PlayerWetnessOverlayTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] arguments) => arguments.Length == 1 ? $"{arguments[0]}%" : key);
        Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores shared language state after each test.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }
    /// <summary>Missing, dry, invalid, and negative samples remain hidden; valid samples clamp to saturation.</summary>
    [Theory]
    [InlineData(0, null)] [InlineData(-1, null)] [InlineData(float.NaN, null)]
    [InlineData(float.PositiveInfinity, null)] [InlineData(.25f, .25f)] [InlineData(2, 1f)]
    public void SamplesNativeWatchedWetness(float raw, float? expected)
    {
        var fixture = CreateFixture();
        using var overlay = new PlayerWetnessOverlay();
        overlay.BeginSession(fixture.Api.Object);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        Assert.Null(overlay.Wetness);
        fixture.Entity.WatchedAttributes.SetFloat("wetness", raw);
        overlay.Refresh();
        Assert.Equal(expected, overlay.Wetness);
        Assert.Equal(raw, fixture.Entity.WatchedAttributes.GetFloat("wetness"));
    }

    /// <summary>Drying, disabling and world exit clear eligibility and prepared dimensions.</summary>
    [Fact]
    public void FillUpdatesReuseArtworkAndDryingCollapses()
    {
        var fixture = CreateFixture();
        using var presentation = new PlayerWetnessPresentation(measure: (_, _) => new(30, 14), loadBackdrop: _ => null);
        using var overlay = new PlayerWetnessOverlay(presentation); overlay.BeginSession(fixture.Api.Object);
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        fixture.Entity.WatchedAttributes.SetFloat("wetness", .25f); overlay.Refresh(); overlay.Prepare(context);
        Assert.False(overlay.Measure().IsEmpty);
        fixture.Entity.WatchedAttributes.SetFloat("wetness", .75f); overlay.Refresh(); overlay.Prepare(context);
        Assert.Equal("75%", presentation.Reading);
        fixture.Entity.WatchedAttributes.SetFloat("wetness", 0); overlay.Refresh(); Assert.True(overlay.Measure().IsEmpty);
        fixture.Entity.WatchedAttributes.SetFloat("wetness", 1); overlay.Refresh(); overlay.Prepare(context); overlay.SetEnabled(false);
        Assert.Null(overlay.Wetness); Assert.True(overlay.Measure().IsEmpty);
        overlay.SetEnabled(true); overlay.Refresh(); overlay.Prepare(context); overlay.EndSession(); Assert.Null(overlay.Wetness);
    }



    /// <summary>Creative, spectator, and dead players release wetness state.</summary>
    [Theory]
    [InlineData(EnumGameMode.Creative)] [InlineData(EnumGameMode.Spectator)]
    public void IneligibleGameModesRemainHidden(EnumGameMode mode)
    {
        var fixture = CreateFixture();
        fixture.Entity.WatchedAttributes.SetFloat("wetness", 1);
        fixture.Player.Object.WorldData.CurrentGameMode = mode;
        using var overlay = new PlayerWetnessOverlay(); overlay.BeginSession(fixture.Api.Object);
        Assert.False(overlay.IsApplicable()); Assert.Null(overlay.Wetness);
        fixture.Player.Object.WorldData.CurrentGameMode = EnumGameMode.Survival;
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
    }


    #endregion

    #region Private
    /// <summary>Supplies a living survival player with mutable native game-mode metadata.</summary>
    private static BowAmmunitionSamplerTests.Fixture CreateFixture()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        var worldData = new Mock<IWorldPlayerData>();
        worldData.SetupProperty(value => value.CurrentGameMode, EnumGameMode.Survival);
        fixture.Player.SetupGet(value => value.WorldData).Returns(worldData.Object);
        fixture.Entity.Alive = true;
        return fixture;
    }
    #endregion
}
