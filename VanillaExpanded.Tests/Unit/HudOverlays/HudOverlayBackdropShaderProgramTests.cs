using Moq;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks lazy backdrop ownership and stage construction without invoking graphics operations.</summary>
public sealed class HudOverlayBackdropShaderProgramTests
{
    #region Public API
    /// <summary>The custom program supplies only its file-backed stages and does not compile during construction.</summary>
    [Fact]
    public void ConstructorCreatesStagesWithoutGpuWork()
    {
        var api = new Mock<ICoreClientAPI>(); var shaders = new Mock<IShaderAPI>();
        var vertex = new Shader(); var fragment = new Shader();
        api.SetupGet(value => value.Shader).Returns(shaders.Object);
        shaders.Setup(value => value.NewShader(EnumShaderType.VertexShader)).Returns(vertex);
        shaders.Setup(value => value.NewShader(EnumShaderType.FragmentShader)).Returns(fragment);
        var program = new HudOverlayBackdropShaderProgram(api.Object);
        Assert.Same(vertex, program.VertexShader); Assert.Same(fragment, program.FragmentShader);
        Assert.Null(program.GeometryShader); Assert.Equal(0, program.ProgramId);
        Assert.Equal("vanillaexpanded", program.AssetDomain);
        Assert.Equal(2, shaders.Invocations.Count);
    }

    /// <summary>An unused client service neither allocates nor subscribes and can be disposed repeatedly.</summary>
    [Fact]
    public void IdleBackdropOwnsNoGraphicsResources()
    {
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        var backdrop = new HudOverlayIconBackdrop(api.Object);
        var renderer = new Mock<IRenderAPI>(MockBehavior.Strict);
        backdrop.Queue(renderer.Object, 10, 20, 2);
        backdrop.Dispose(); backdrop.Dispose();
        Assert.Empty(api.Invocations); Assert.Empty(renderer.Invocations);
    }

    /// <summary>Collection intersects each circle, rolls back failed members, and drains without GPU resources.</summary>
    [Fact]
    public void CollectionClipsRollsBackAndClearsWithoutGraphicsCalls()
    {
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        var renderer = new Mock<IRenderAPI>(MockBehavior.Strict);
        using var backdrop = new HudOverlayIconBackdrop(api.Object);
        var pending = (System.Collections.IList)typeof(HudOverlayIconBackdrop)
            .GetField("pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(backdrop)!;
        backdrop.Collect(new System.Drawing.RectangleF(10, 10, 10, 10), () =>
        {
            backdrop.Queue(renderer.Object, 0, 0, 1);
            backdrop.Queue(renderer.Object, 100, 100, 1);
        });
        var entry = Assert.Single(pending.Cast<(System.Drawing.RectangleF Circle, System.Drawing.RectangleF Clip)>());
        Assert.Equal(new System.Drawing.RectangleF(0, 0, 24, 24), entry.Circle);
        Assert.Equal(new System.Drawing.RectangleF(10, 10, 10, 10), entry.Clip);
        Assert.Throws<InvalidOperationException>(() => backdrop.Collect(new(0, 0, 100, 100), () =>
        {
            backdrop.Queue(renderer.Object, 30, 30, 1);
            throw new InvalidOperationException();
        }));
        Assert.Single(pending.Cast<object>());
        backdrop.Queue(renderer.Object, 0, 0, 1);
        Assert.Single(pending.Cast<object>());
        backdrop.Flush(renderer.Object);
        Assert.Empty(pending);
        backdrop.Collect(new(0, 0, 100, 100), () => backdrop.Queue(renderer.Object, 0, 0, 1));
        backdrop.Clear();
        Assert.Empty(pending); Assert.Empty(api.Invocations); Assert.Empty(renderer.Invocations);
    }

    /// <summary>Every status presentation draws its shared backdrop before artwork and removes it with hidden content.</summary>
    [Theory]
    [InlineData("temperature")]
    [InlineData("weather")]
    [InlineData("time")]
    [InlineData("rift")]
    public void StatusPresentationsBorrowBackdropAndHideTogether(string kind)
    {
        var api = new Mock<ICoreClientAPI>(); var renderer = new Mock<IRenderAPI>();
        var backdrop = new Mock<IHudOverlayIconBackdrop>(); var calls = new List<string>();
        backdrop.Setup(value => value.Queue(renderer.Object, It.IsAny<double>(), It.IsAny<double>(), 1))
            .Callback(() => calls.Add("backdrop"));
        renderer.Setup(value => value.RenderTexture(It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vintagestory.API.MathTools.Vec4f>()))
            .Callback(() => calls.Add("icon"));
        var context = new VanillaExpanded.HudOverlays.Registration.HudOverlayPreparationContext(api.Object, 1, "en");
        var bounds = ElementBounds.Fixed(0, 0, 51, 46).WithEmptyParent(); bounds.CalcWorldBounds();
        /// <summary>Provides borrowed placeholder handles without allocating textures.</summary>
        LoadedTexture[] Load(VanillaExpanded.HudOverlays.Registration.HudOverlayPreparationContext value) =>
            Enumerable.Range(1, 7).Select(id => new LoadedTexture(value.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray();
        Action prepare; Action collect; Action draw; Action reset; IDisposable owner;
        // Exercise the shared borrowing boundary in each production presentation, replacing only graphics services.
        switch (kind)
        {
            case "temperature":
                var temperature = new VanillaExpanded.BodyTemperature.BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(), Load, _ => backdrop.Object);
                temperature.SetContent(new(34, 37, VanillaExpanded.BodyTemperature.BodyTemperatureRisk.Cold, .25f), false);
                collect = () => temperature.CollectBackdrops(renderer.Object, bounds); owner = temperature; prepare = () => temperature.Prepare(context);
                draw = () => temperature.Draw(renderer.Object, bounds, System.Drawing.RectangleF.Empty, 0); reset = temperature.Reset;
                break;
            case "weather":
                var weather = new VanillaExpanded.WeatherConditions.WeatherConditionsPresentation(Load, _ => backdrop.Object);
                weather.SetContent(new(Vintagestory.GameContent.EnumPrecipitationType.Rain, false));
                collect = () => weather.CollectBackdrops(renderer.Object, bounds); owner = weather; prepare = () => weather.Prepare(context); draw = () => weather.Draw(renderer.Object, bounds); reset = weather.Reset;
                break;
            case "time":
                var time = new VanillaExpanded.ApproximateTime.ApproximateTimePresentation(Load, _ => backdrop.Object);
                time.SetContent(VanillaExpanded.ApproximateTime.ApproximateTimePeriod.Noon);
                collect = () => time.CollectBackdrops(renderer.Object, bounds); owner = time; prepare = () => time.Prepare(context); draw = () => time.Draw(renderer.Object, bounds); reset = time.Reset;
                break;
            default:
                var rift = new VanillaExpanded.RiftActivity.RiftActivityPresentation(Load, _ => backdrop.Object);
                rift.SetContent(VanillaExpanded.RiftActivity.RiftActivityLevel.Low);
                collect = () => rift.CollectBackdrops(renderer.Object, bounds); owner = rift; prepare = () => rift.Prepare(context); draw = () => rift.Draw(renderer.Object, bounds); reset = rift.Reset;
                break;
        }
        using (owner)
        {
            prepare(); collect(); draw(); Assert.Equal("backdrop", calls[0]); Assert.Contains("icon", calls);
            var background = Assert.Single(backdrop.Invocations);
            var artwork = renderer.Invocations.First(call => call.Method.Name == "RenderTexture");
            Assert.Equal((double)background.Arguments[1] + (kind == "temperature" ? 5 : 0), artwork.Arguments[1]);
            Assert.Equal(background.Arguments[2], artwork.Arguments[2]);
            calls.Clear(); reset(); collect(); draw(); Assert.Empty(calls);
        }
    }
    #endregion
}
