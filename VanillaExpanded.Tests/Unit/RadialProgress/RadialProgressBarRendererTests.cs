using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RadialProgress;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.RadialProgress;

[Trait("Category", "Unit")]
public sealed class RadialProgressBarRendererTests
{
    [Theory]
    [InlineData(1920, 1080, 101, 21, 1f, "AutoStashing")]
    [InlineData(1921, 1081, 100, 20, 1f, "AutoLooting")]
    [InlineData(1921, 1081, 101, 21, 1.25f, "AutoStashing")]
    [InlineData(1920, 1080, 101, 21, 1.5f, "AutoLooting")]
    [InlineData(1921, 1081, 101, 21, 2f, "AutoStashing")]
    public void CachedLabelDrawsAtIntegerPixelsWithoutResizingOrRerasterizing(
        int frameWidth, int frameHeight, int textureWidth, int textureHeight, float scale, string label)
    {
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        var render = new Mock<IRenderAPI>();
        var shader = new Mock<IShaderProgram>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        render.Setup(value => value.GetEngineShader(EnumShaderProgram.Gui)).Returns(shader.Object);
        using var texture = new LoadedTexture(api.Object, 0, textureWidth, textureHeight);
        // Bypass GPU initialization and seed an already-cached raster to test the actual draw boundary.
        var renderer = (RadialProgressBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(RadialProgressBarRenderer));
        SetField(renderer, "capi", api.Object);
        SetField(renderer, "textTexture", texture);
        SetField(renderer, "renderedText", label);
        renderer.Text = label;
        var drawText = typeof(RadialProgressBarRenderer).GetMethod("DrawText", BindingFlags.Instance | BindingFlags.NonPublic)!;
        float size = 64 * scale;
        var rectangles = new[]
        {
            (X: (frameWidth - size) / 2f, Y: (frameHeight - size) / 2f, Width: size, Height: size),
            (X: 10.25f * scale, Y: 20.75f * scale, Width: 63.5f * scale, Height: 65.25f * scale),
            (X: -10.75f * scale, Y: -20.25f * scale, Width: 64.25f * scale, Height: 63.5f * scale)
        };
        foreach (var rectangle in rectangles)
        {
            renderer.SetRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
            double naturalX = rectangle.X + (rectangle.Width - textureWidth) / 2d;
            double naturalY = (double)rectangle.Y + rectangle.Height + 4;
            render.Invocations.Clear();
            for (int frame = 0; frame < 3; frame++) drawText.Invoke(renderer, null);
            var draws = render.Invocations.Where(call => call.Method.Name == nameof(IRenderAPI.Render2DLoadedTexture)).ToArray();
            Assert.Equal(3, draws.Length);
            foreach (var draw in draws)
            {
                Assert.Same(texture, draw.Arguments[0]);
                float x = Assert.IsType<float>(draw.Arguments[1]);
                float y = Assert.IsType<float>(draw.Arguments[2]);
                Assert.Equal(MathF.Round(x), x);
                Assert.Equal(MathF.Round(y), y);
                Assert.InRange(Math.Abs(x - naturalX), 0, .5);
                Assert.InRange(Math.Abs(y - naturalY), 0, .5);
                Assert.Equal(50f, draw.Arguments[3]);
            }
            Assert.Equal(rectangle.X, renderer.ScreenX);
            Assert.Equal(rectangle.Y, renderer.ScreenY);
            Assert.Equal(rectangle.Width, renderer.Width);
            Assert.Equal(rectangle.Height, renderer.Height);
            Assert.Equal(textureWidth, texture.Width);
            Assert.Equal(textureHeight, texture.Height);
            Assert.False(texture.Disposed);
        }
        shader.Verify(value => value.Use(), Times.Exactly(rectangles.Length * 3));
        shader.Verify(value => value.Stop(), Times.Exactly(rectangles.Length * 3));
    }

    private static void SetField(RadialProgressBarRenderer renderer, string name, object value) =>
        typeof(RadialProgressBarRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(renderer, value);
}
