using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks that radial instruments share the same housing assets and aperture registration.</summary>
[Collection("HudOverlayGeometry")]
public sealed class RadialGaugeArtworkTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    /// <summary>Provides an isolated language service for the real preparation boundaries.</summary>
    public RadialGaugeArtworkTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("9°C");
        Lang.AvailableLanguages["en"] = service.Object;
    }

    /// <summary>Restores the shared native language registry after each test.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Clock and thermometer borrow identical generic backing/glass paths while retaining their own mechanisms.</summary>
    [Fact]
    public void SharedHousingAssetPaths()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var renderer = new Mock<IRenderAPI>(); api.SetupGet(value => value.Render).Returns(renderer.Object);
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        using var clock = new ApproximateTimePresentation(dial: Mock.Of<IApproximateTimeDialRenderer>(),
            createFace: (preparation, _) => new LoadedTexture(preparation.Api));
        clock.SetContent(ApproximateTimePeriod.Noon, .5); clock.Prepare(context);
        using var text = new WorkshopGaugeText((preparation, _) => new LoadedTexture(preparation.Api) { Width = 30, Height = 14 });
        using var thermometer = new EnvironmentalTemperaturePresentation(text, createTicks: preparation => new LoadedTexture(preparation.Api),
            liquid: new EnvironmentalTemperatureLiquidMask(_ => new int[512 * 512], texture => texture));
        thermometer.SetContent(9); thermometer.Prepare(context);
        var paths = renderer.Invocations.Where(call => call.Method.Name == "GetOrLoadTexture")
            .Select(call => ((AssetLocation)call.Arguments[0]).Path).ToArray();
        const string root = "textures/hud/workshop-gauges/";
        Assert.Equal(2, paths.Count(path => path == root + "radial/layer-0.png"));
        Assert.Equal(2, paths.Count(path => path == root + "radial/layer-2.png"));
        Assert.Contains(root + "clock/layer-1.png", paths);
        Assert.Contains(root + "thermometer/empty-glass.png", paths);
        Assert.Contains(root + "thermometer/clamps.png", paths);
        Assert.DoesNotContain(paths, path => path == root + "clock/layer-0.png" || path == root + "thermometer/layer-0.png");
    }

    /// <summary>The thermometer mechanism stays inside its supplied housing at arbitrary instrument sizes.</summary>
    [Theory]
    [InlineData(45)] [InlineData(90)] [InlineData(180)]
    public void MechanismRemainsInsideSuppliedHousing(float size)
    {
        var housing = new System.Drawing.RectangleF(13, 27, size, size);
        var mechanism = EnvironmentalTemperatureGaugeLayout.MechanismBounds(housing);
        Assert.True(housing.Contains(mechanism));
    }
    #endregion
}
