using Moq;
using VanillaExpanded.AnimalSexIndicators;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AnimalSexIndicators;

/// <summary>Checks that per-animal drawing preserves the caller's shader activation.</summary>
public sealed class AnimalSexIndicatorDrawTests
{
    #region Public API
    /// <summary>Leaves activation and camera uniforms untouched during successful and failed symbol draws.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawPreservesFrameShaderActivation(bool failDraw)
    {
        bool active = true;
        int tintWrites = 0;
        var shader = new Mock<IShaderProgram>();
        var render = new Mock<IRenderAPI>();
        shader.Setup(program => program.Use()).Callback(() => active = true);
        shader.Setup(program => program.Stop()).Callback(() => active = false);
        shader.Setup(program => program.Uniform("iconTint", It.IsAny<Vec4f>())).Callback<string, Vec4f>((name, color) =>
        {
            // Match the engine guard responsible for the original inactive-uniform crash.
            if (!active) throw new InvalidOperationException("Can't set uniform on not active shader!");
            tintWrites++;
        });
        render.Setup(api => api.RenderMesh(It.IsAny<MeshRef>())).Callback(() =>
        {
            Assert.True(active);
            if (failDraw) throw new ApplicationException("Mesh draw failed");
        });
        Action draw = () => AnimalSexIndicatorDraw.Draw(render.Object, shader.Object, null!, 7,
            new float[16], new Vec4f(0.3f, 0.5f, 0.9f, 1));
        if (failDraw) Assert.Throws<ApplicationException>(draw);
        else
        {
            draw();
            draw();
        }
        Assert.True(active);
        Assert.Equal(failDraw ? 1 : 2, tintWrites);
        shader.Verify(program => program.Use(), Times.Never);
        shader.Verify(program => program.Stop(), Times.Never);
        shader.Verify(program => program.UniformMatrix("viewMatrix", It.IsAny<float[]>()), Times.Never);
        shader.Verify(program => program.UniformMatrix("projectionMatrix", It.IsAny<float[]>()), Times.Never);
    }
    #endregion
}
