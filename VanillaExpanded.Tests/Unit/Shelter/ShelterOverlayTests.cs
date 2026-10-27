using Moq;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.Shelter;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.Tests.Unit.HudOverlays;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.Shelter;

[Trait("Category", "Unit")]
[Collection("HudOverlayGeometry")]
public sealed class ShelterOverlayTests
{
    [Theory]
    [InlineData(0, 10, 0, true)]
    [InlineData(0, 0, 0, true)]
    [InlineData(1, 0, 10, true)]
    [InlineData(4, 9, 10, true)]
    [InlineData(1, 10, 10, false)]
    [InlineData(1, 11, 10, false)]
    [InlineData(1, 0, 0, false)]
    public void UsesNativeRoomAndCaveProtection(int exits, int skylight, int nonSkylight, bool expected)
    {
        var fixture = CreateFixture();
        var room = new Room { ExitCount = exits, SkylightCount = skylight, NonSkylightCount = nonSkylight };
        using var overlay = new ShelterOverlay(findRoom: _ => room);
        overlay.BeginSession(fixture.Api.Object);
        var change = overlay.Refresh();
        Assert.Equal(expected, overlay.Sheltered);
        Assert.Equal(expected ? HudOverlayChange.Presentation | HudOverlayChange.Measurement : HudOverlayChange.None, change);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(exits, room.ExitCount);
        Assert.Equal(skylight, room.SkylightCount);
        Assert.Equal(nonSkylight, room.NonSkylightCount);
    }

    [Fact]
    public void SamplesCurrentPositionAndDimensionOnlyDuringRefresh()
    {
        var fixture = CreateFixture();
        int samples = 0;
        using var presentation = CreatePresentation();
        using var overlay = new ShelterOverlay(presentation, position =>
        {
            samples++;
            Assert.Equal(fixture.Entity.Pos.AsBlockPos, position);
            return new Room { ExitCount = 0 };
        });
        overlay.BeginSession(fixture.Api.Object);
        fixture.Entity.Pos.SetPos(-1.25, 100.5, 23.75);
        fixture.Entity.Pos.Dimension = 1;
        Assert.True(overlay.IsApplicable());
        Assert.Equal(0, samples);
        overlay.Refresh();
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.Prepare(context);
        overlay.Measure();
        overlay.BeginFrame(.1f);
        using var view = overlay.CreateView(fixture.Api.Object, NativeViewRole.Live);
        Assert.IsType<IconReadingView>(view);
        Assert.Equal(1, samples);
        fixture.Entity.Pos.SetPos(30, 80, 40);
        fixture.Entity.Pos.Dimension = 0;
        overlay.Refresh();
        Assert.Equal(2, samples);
    }

    [Fact]
    public void ExposureMissingRoomsAndWorldExitReleaseFootprintAndAllowReentry()
    {
        var fixture = CreateFixture();
        Room? room = new() { ExitCount = 0 };
        using var presentation = CreatePresentation();
        using var overlay = new ShelterOverlay(presentation, _ => room);
        overlay.BeginSession(fixture.Api.Object);
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.Refresh();
        overlay.Prepare(context);
        Assert.False(overlay.Measure().IsEmpty);
        room = new Room { ExitCount = 1, SkylightCount = 10, NonSkylightCount = 0 };
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        overlay.Prepare(context);
        Assert.False(overlay.Sheltered);
        Assert.True(overlay.Measure().IsEmpty);
        room = new Room { ExitCount = 0 };
        overlay.Refresh();
        overlay.Prepare(context);
        room = null;
        overlay.Refresh();
        Assert.False(overlay.Sheltered);
        Assert.True(overlay.Measure().IsEmpty);
        room = new Room { ExitCount = 0 };
        overlay.Refresh();
        overlay.Prepare(context);
        overlay.EndSession();
        Assert.False(overlay.Sheltered);
        Assert.True(overlay.Measure().IsEmpty);
        overlay.BeginSession(fixture.Api.Object);
        Assert.False(overlay.Sheltered);
        overlay.Refresh();
        Assert.True(overlay.Sheltered);
    }

    [Theory]
    [InlineData(EnumGameMode.Creative)]
    [InlineData(EnumGameMode.Spectator)]
    public void IneligiblePlayersAndDisabledFeatureDoNotSample(EnumGameMode mode)
    {
        var fixture = CreateFixture();
        int samples = 0;
        using var overlay = new ShelterOverlay(findRoom: _ => { samples++; return new Room { ExitCount = 0 }; });
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        Assert.True(overlay.Sheltered);
        fixture.Player.Object.WorldData.CurrentGameMode = mode;
        Assert.False(overlay.IsApplicable());
        Assert.False(overlay.Sheltered);
        overlay.Refresh();
        fixture.Player.Object.WorldData.CurrentGameMode = EnumGameMode.Survival;
        fixture.Entity.Alive = false;
        overlay.Refresh();
        fixture.Entity.Alive = true;
        overlay.SetEnabled(false);
        overlay.Refresh();
        Assert.Equal(1, samples);
        overlay.SetEnabled(true);
        overlay.Refresh();
        Assert.True(overlay.Sheltered);
        Assert.Equal(2, samples);
    }

    [Fact]
    public void VisibilityCanReverseWhileRetainingPreparedFootprint()
    {
        var fixture = CreateFixture();
        Room? room = new() { ExitCount = 0 };
        using var presentation = CreatePresentation();
        using var overlay = new ShelterOverlay(presentation, _ => room);
        overlay.Visibility.Enable();
        overlay.BeginSession(fixture.Api.Object);
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.Refresh();
        overlay.Prepare(context);
        room = null;
        overlay.Refresh();
        Assert.False(overlay.Sheltered);
        Assert.True(overlay.Visibility.Exiting);
        Assert.False(overlay.Measure().IsEmpty);
        room = new Room { ExitCount = 0 };
        overlay.Refresh();
        overlay.Prepare(context);
        Assert.True(overlay.Visibility.Renderable);
        Assert.False(overlay.Visibility.Exiting);
        overlay.SetEnabled(false);
        Assert.True(overlay.Visibility.Exiting);
        overlay.EndSession();
        Assert.False(overlay.Visibility.Renderable);
        Assert.True(overlay.Measure().IsEmpty);
    }

    [Fact]
    public void TextStyleUsesLocalizedContentWithoutLoadingArtwork()
    {
        var fixture = CreateFixture();
        var previous = Lang.AvailableLanguages.GetValueOrDefault("en");
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = language.Object;
        try
        {
            int backdropLoads = 0;
            using var presentation = new ShelterPresentation(measure: (_, _) => new(35, 14),
                loadBackdrop: _ => { backdropLoads++; return null; });
            using var overlay = new ShelterOverlay(presentation, _ => new Room { ExitCount = 0 });
            overlay.BeginSession(fixture.Api.Object);
            overlay.Refresh();
            var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
            overlay.Prepare(context);
            overlay.SetStyle(HudIndicatorStyle.Text);
            Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
            overlay.Prepare(context);
            Assert.True(overlay.Sheltered);
            Assert.False(overlay.Measure().IsEmpty);
            using var view = overlay.CreateView(fixture.Api.Object, NativeViewRole.Live);
            Assert.IsType<TextView>(view);
            Assert.Equal(1, backdropLoads);
            language.Verify(value => value.Get("vanillaexpanded:shelter-reading", It.IsAny<object[]>()), Times.Once);
            language.Verify(value => value.Get("vanillaexpanded:hud-short-shelter", It.IsAny<object[]>()), Times.Once);
        }
        finally
        {
            if (previous == null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = previous;
        }
    }

    [Fact]
    public void IconViewOwnsAndReusesArtworkAcrossPreparation()
    {
        using var fixture = new HudNativeComponentFixture();
        var artwork = new List<LoadedTexture>();
        using var presentation = new ShelterPresentation(load: (context, path) =>
        {
            Assert.Equal("textures/hud/shelter.svg", path);
            var texture = new LoadedTexture(context.Api, 100 + artwork.Count, 40, 40);
            artwork.Add(texture);
            return new(texture, true);
        }, loadBackdrop: _ => null);
        presentation.Prepare(fixture.Context);
        var first = presentation.CreateView(fixture.Api.Object);
        using var second = presentation.CreateView(fixture.Api.Object);
        first.Prepare(fixture.Context);
        first.Prepare(fixture.Context);
        second.Prepare(fixture.Context);
        Assert.Equal(2, artwork.Count);
        Assert.NotSame(first, second);
        first.Dispose();
        Assert.Equal(0, artwork[0].TextureId);
        Assert.NotEqual(0, artwork[1].TextureId);
        second.Dispose();
        Assert.Equal(0, artwork[1].TextureId);
    }

    private static ShelterPresentation CreatePresentation() => new(loadBackdrop: _ => null);

    private static BowAmmunitionSamplerTests.Fixture CreateFixture()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        var worldData = new Mock<IWorldPlayerData>();
        worldData.SetupProperty(value => value.CurrentGameMode, EnumGameMode.Survival);
        fixture.Player.SetupGet(value => value.WorldData).Returns(worldData.Object);
        fixture.Entity.Alive = true;
        return fixture;
    }
}
