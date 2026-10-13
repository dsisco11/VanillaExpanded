using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.PlayerWetness;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.PlayerWetness;

/// <summary>Checks native wetness sampling, fill geometry, and cached artwork lifetime.</summary>
[Collection("HudOverlayGeometry")]
public sealed class PlayerWetnessOverlayTests
{
    #region Public API
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

    /// <summary>Fill-only updates reuse artwork; drying, disabling, and world exit release bindings without reading gameplay during draw.</summary>
    [Fact]
    public void FillUpdatesReuseArtworkAndDryingCollapses()
    {
        var fixture = CreateFixture();
        int builds = 0;
        var artwork = new List<LoadedTexture>();
        using var presentation = new PlayerWetnessPresentation(context =>
        {
            builds++;
            var textures = new[] { new LoadedTexture(context.Api) { TextureId = 1 }, new LoadedTexture(context.Api) { TextureId = 2 } };
            artwork.AddRange(textures); return textures;
        }, _ => null);
        using var overlay = new PlayerWetnessOverlay(presentation);
        overlay.BeginSession(fixture.Api.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", contentScale: 1.5);
        fixture.Entity.WatchedAttributes.SetFloat("wetness", .25f);
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(new SizeF(24, 24), overlay.Measure());
        fixture.Entity.WatchedAttributes.SetFloat("wetness", .75f);
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(1, builds);
        fixture.World.Invocations.Clear(); fixture.Player.Invocations.Clear();
        var renderer = new Mock<IRenderAPI>();
        var bounds = Bounds();
        overlay.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), 0);
        Assert.Empty(fixture.World.Invocations); Assert.Empty(fixture.Player.Invocations);
        Assert.Equal(2, renderer.Invocations.Count(call => call.Method.Name == "Render2DTexture"));
        fixture.Entity.WatchedAttributes.SetFloat("wetness", 0);
        overlay.Refresh();
        Assert.Equal(SizeF.Empty, overlay.Measure()); Assert.All(artwork, texture => Assert.NotEqual(0, texture.TextureId));
        fixture.Entity.WatchedAttributes.SetFloat("wetness", 1);
        overlay.Refresh(); overlay.Prepare(preparation); overlay.SetEnabled(false);
        Assert.Null(overlay.Wetness); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.SetEnabled(true); overlay.Refresh(); overlay.Prepare(preparation); overlay.EndSession();
        Assert.Null(overlay.Wetness); Assert.All(artwork, texture => Assert.NotEqual(0, texture.TextureId));
    }

    /// <summary>The drop's crop grows continuously with wetness and restores scissor state after a render failure.</summary>
    [Theory]
    [InlineData(.01f)] [InlineData(.5f)] [InlineData(1)]
    public void FillCropMatchesWetnessAndRestoresScissor(float wetness)
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        using var presentation = new PlayerWetnessPresentation(context =>
            [new LoadedTexture(context.Api) { TextureId = 1 }, new LoadedTexture(context.Api) { TextureId = 2 }], _ => null);
        presentation.SetContent(wetness); presentation.Prepare(new(api.Object, 1, "en", contentScale: 1.5));
        var bounds = Bounds();
        var renderer = new Mock<IRenderAPI>();
        double cropTop = -1;
        renderer.Setup(value => value.PushScissor(It.IsAny<ElementBounds>(), true))
            .Callback<ElementBounds, bool>((crop, _) => cropTop = crop.renderY);
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 36, 36));
        Assert.Equal(36 * (120d - 110d * wetness) / 128, cropTop, 5);
        renderer.Verify(value => value.PopScissor(), Times.Once);
        renderer.Setup(value => value.Render2DTexture(2, It.IsAny<float>(), It.IsAny<float>(),
            It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vintagestory.API.MathTools.Vec4f>()))
            .Throws(new InvalidOperationException("draw failure"));
        Assert.Throws<InvalidOperationException>(() => presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 36, 36)));
        renderer.Verify(value => value.PopScissor(), Times.Exactly(2));
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

    /// <summary>PNG layers load from the engine cache without transferring texture ownership.</summary>
    [Fact]
    public void DropletLayersLoadTextureAssetsWithoutOwningCacheStorage()
    {
        var api = new Mock<ICoreClientAPI>();
        var renderer = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Render).Returns(renderer.Object);
        var paths = new List<string>();
        renderer.Setup(value => value.GetOrLoadTexture(It.IsAny<AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new TextureLoad((AssetLocation location, ref LoadedTexture texture) => paths.Add(location.ToString())));
        var textures = PlayerWetnessIconTextures.Create(new(api.Object, 1, "en"));
        Assert.Equal(new[] { "vanillaexpanded:textures/hud/player-wetness/outline.png",
            "vanillaexpanded:textures/hud/player-wetness/fill-mask.png" }, paths);
        Assert.All(textures, texture => Assert.True(texture.IgnoreUndisposed));
        api.Verify(value => value.Gui, Times.Never);
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
    /// <summary>Supplies absolute pixel bounds without depending on the engine's global GUI scale.</summary>
    private static ElementBounds Bounds()
    {
        var bounds = ElementBounds.Fixed(0, 0, 36, 36).WithEmptyParent();
        bounds.absFixedX = 0; bounds.absFixedY = 0; bounds.absInnerWidth = 36; bounds.absInnerHeight = 36; bounds.Initialized = true;
        return bounds;
    }
    /// <summary>Matches the engine cached-texture load boundary.</summary>
    private delegate void TextureLoad(AssetLocation location, ref LoadedTexture texture);
    #endregion
}


