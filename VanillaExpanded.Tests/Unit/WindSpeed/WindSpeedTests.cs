using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.WindSpeed;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.WindSpeed;

/// <summary>Checks wind band boundaries and native sampling independently of composite weather.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WindSpeedTests
{
    #region Public API
    /// <summary>Exact entry and exit boundaries remain inactive while values above them activate the band.</summary>
    [Theory]
    [InlineData(.05f, 0, 0)]
    [InlineData(.051f, 0, 1)]
    [InlineData(.30f, 0, 1)]
    [InlineData(.301f, 0, 2)]
    [InlineData(.70f, 0, 2)]
    [InlineData(.701f, 0, 3)]
    [InlineData(.035f, 1, 0)]
    [InlineData(.036f, 1, 1)]
    [InlineData(.25f, 2, 1)]
    [InlineData(.251f, 2, 2)]
    [InlineData(.60f, 3, 2)]
    [InlineData(.601f, 3, 3)]
    [InlineData(float.NaN, 3, 0)]
    [InlineData(float.PositiveInfinity, 3, 0)]
    [InlineData(-1, 3, 0)]
    public void StableBandBoundaries(float speed, int previous, int expected)
    {
        Assert.Equal(expected, WindSpeedSample.Classify(speed, previous));
    }

    /// <summary>Sampling needs only local wind, reuses stable readings, and clears history across eligibility changes.</summary>
    [Fact]
    public void NativeWindDoesNotRequireWeatherOrClimate()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var blocks = new Mock<IBlockAccessor>(MockBehavior.Strict);
        var wind = new Vec3d(.8, 0, 0);
        blocks.Setup(value => value.GetWindSpeedAt(It.IsAny<Vec3d>())).Returns(() => wind);
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        using var overlay = new WindSpeedOverlay();
        overlay.BeginSession(fixture.Api.Object);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(3, overlay.Intensity);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        wind = new Vec3d(.65, 0, 0);
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(3, overlay.Intensity);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable());
        Assert.Equal(0, overlay.Intensity);
        overlay.SetEnabled(true);
        overlay.Refresh();
        Assert.Equal(2, overlay.Intensity);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
        Assert.Equal(0, overlay.Intensity);
        overlay.EndSession();
        Assert.False(overlay.IsApplicable());
        blocks.VerifyAll();
    }

    /// <summary>Text mode avoids artwork, icon mode caches its owned SVG, and style changes release old textures.</summary>
    [Fact]
    public void PresentationStylesOwnAndReleaseTheirResources()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock }.Object;
        var texts = new List<LoadedTexture>();
        var icons = new List<LoadedTexture>();
        var values = new List<string>();
        /// <summary>Creates tracked text textures without invoking native font rendering.</summary>
        LoadedTexture CreateText(HudOverlayPreparationContext context, string value)
        {
            values.Add(value);
            var texture = new LoadedTexture(context.Api) { TextureId = 1, Width = 20, Height = 10 };
            texts.Add(texture);
            return texture;
        }
        System.Func<HudOverlayPreparationContext, LoadedTexture> load = context =>
        {
            var texture = new LoadedTexture(context.Api) { TextureId = 2, Width = 64, Height = 64 };
            icons.Add(texture);
            return texture;
        };
        using var presentation = new WindSpeedPresentation(load,
            new HudIndicatorTextPresentation(new WorkshopGaugeText(CreateText), new WorkshopGaugeText(CreateText)), new HudOverlayIconTextPresentation(CreateText));
        var preparation = new HudOverlayPreparationContext(api, 1, "en");
        presentation.SetContent(2);
        presentation.SetStyle(HudIndicatorStyle.Text);
        presentation.Prepare(preparation);
        Assert.Empty(icons);
        Assert.Contains(values, value => value.Contains("wind-speed-moderate"));
        presentation.SetStyle(HudIndicatorStyle.Icon);
        Assert.All(texts, texture => Assert.True(texture.Disposed));
        presentation.Prepare(preparation);
        presentation.Prepare(preparation);
        Assert.Single(icons);
        presentation.Reset();
        Assert.All(icons, texture => Assert.True(texture.Disposed));
        Assert.All(texts, texture => Assert.True(texture.Disposed));
        presentation.Prepare(preparation);
        Assert.Single(icons);
    }

    /// <summary>Native text dimensions survive scaling while the reading and icon stay centered and separated.</summary>
    [Theory]
    [InlineData(1, 190, 23)]
    [InlineData(2, 430, 49)]
    public void DrawingForwardsFullReadingDimensions(double scale, int width, int height)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock }.Object;
        using var presentation = new WindSpeedPresentation(context => new LoadedTexture(context.Api)
            { TextureId = 1, Width = 64, Height = 64 }, label: new HudOverlayIconTextPresentation((context, _) =>
                new LoadedTexture(context.Api) { TextureId = 2, Width = width, Height = height }));
        presentation.SetContent(3);
        presentation.Prepare(new(api, scale, "en"));
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
        bounds.absFixedX = 17; bounds.absFixedY = 29;
        bounds.absInnerWidth = presentation.Size.Width * scale + 24;
        bounds.absInnerHeight = presentation.Size.Height * scale + 18;
        bounds.Initialized = true;
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds);
        var icon = Assert.Single(renderer.Invocations, value => value.Method.Name == "RenderTexture" && (int)value.Arguments[0] == 1);
        var text = Assert.Single(renderer.Invocations, value => value.Method.Name == "Render2DTexturePremultipliedAlpha" && (int)value.Arguments[0] == 2);
        Assert.Equal((double)width, Convert.ToDouble(text.Arguments[3]), 4);
        Assert.Equal((double)height, Convert.ToDouble(text.Arguments[4]), 4);
        double center = bounds.renderX + bounds.OuterWidth / 2;
        Assert.Equal(center, Convert.ToDouble(text.Arguments[1]) + width / 2d, 4);
        Assert.Equal(center, Convert.ToDouble(icon.Arguments[1]) + Convert.ToDouble(icon.Arguments[3]) / 2, 4);
        Assert.True(Convert.ToDouble(text.Arguments[2]) >= Convert.ToDouble(icon.Arguments[2]) + Convert.ToDouble(icon.Arguments[4]));
    }
    #endregion
}
