using Moq;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks file-backed dashed-frame stage ownership without creating a graphics context.</summary>
public sealed class HudOverlayDashedBorderShaderProgramTests
{
    #region Public API
    /// <summary>The dedicated program creates its native stages but leaves compilation to the renderer.</summary>
    [Fact]
    public void ConstructorCreatesStagesWithoutGpuWork()
    {
        var api = new Mock<ICoreClientAPI>();
        var shaders = new Mock<IShaderAPI>();
        var vertex = new Shader();
        var fragment = new Shader();
        api.SetupGet(value => value.Shader).Returns(shaders.Object);
        shaders.Setup(value => value.NewShader(EnumShaderType.VertexShader)).Returns(vertex);
        shaders.Setup(value => value.NewShader(EnumShaderType.FragmentShader)).Returns(fragment);
        var program = new HudOverlayDashedBorderShaderProgram(api.Object);
        Assert.Same(vertex, program.VertexShader);
        Assert.Same(fragment, program.FragmentShader);
        Assert.Null(program.GeometryShader);
        Assert.Equal(0, program.ProgramId);
        Assert.Equal(Constants.ModId, program.AssetDomain);
        Assert.Equal(2, shaders.Invocations.Count);
    }
    /// <summary>An unused or disposed renderer never allocates resources or subscribes to reload events.</summary>
    [Theory]
    [InlineData(1, 2, 30, 40)]
    public void UnusedRendererDisposalAndRejectedDrawsNeedNoGraphicsContext(float x, float y, float width, float height)
    {
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        var renderer = new HudOverlayDashedBorder(api.Object);
        renderer.Draw(System.Drawing.RectangleF.Empty, 1);
        renderer.Dispose();
        renderer.Dispose();
        renderer.Draw(new System.Drawing.RectangleF(x, y, width, height), 1);
        Assert.Empty(api.Invocations);
    }
    #endregion
}
