using Cairo;
using Moq;
using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.Gui;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.RadialProgress;
using VanillaExpanded.Tests.Unit.HudOverlays;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.Gui;

[Collection("HudOverlayGeometry")]
[Trait("Category", "Unit")]
public sealed class TextDrawingTests
{
    [Theory]
    [InlineData(1, EnumTextOrientation.Left)]
    [InlineData(1.125, EnumTextOrientation.Center)]
    [InlineData(1.5, EnumTextOrientation.Right)]
    [InlineData(2, EnumTextOrientation.Center)]
    public void TextureCreationPreservesExplicitDimensionsAndUploadMode(float scale, EnumTextOrientation orientation)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        using var font = Font(0);
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), false, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                texture.Width = surface.Width;
                texture.Height = surface.Height;
            }));
        const string text = "Several wrapped words\n8\n";
        using var actual = TextDrawing.CreateTexture(fixture.Api.Object, text, font, 95, 160, orientation);
        Assert.Equal(95, actual.Width);
        Assert.Equal(160, actual.Height);
        fixture.Gui.Verify(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), false,
            ref It.Ref<LoadedTexture>.IsAny), Times.Once);
        fixture.Gui.VerifyGet(gui => gui.TextTexture, Times.Never);
    }

    [Fact]
    public void DrawingRestoresCallerPathColorFontAndLineWidth()
    {
        using var fixture = new HudNativeComponentFixture();
        using var font = Font(1.5);
        using var surface = new ImageSurface(Format.Argb32, 1, 1);
        using var context = new Context(surface);
        context.SetFontSize(9);
        double previousHeight = context.FontExtents.Height;
        context.LineWidth = 7;
        context.SetSourceRGBA(0, 1, 0, 1);
        context.Rectangle(0, 0, 5, 5);
        using var previousSource = context.GetSource();
        TextDrawing.DrawTextLine(context, font, "Outlined", 30, 10);
        Assert.Equal(7, context.LineWidth);
        Assert.Equal(previousHeight, context.FontExtents.Height);
        Assert.True(context.InFill(2, 2));
        Assert.False(context.InFill(6, 6));
        using var restoredSource = context.GetSource();
        Assert.Equal(previousSource.Handle, restoredSource.Handle);
    }

    [Fact]
    public void UploadFailureDisposesPartialTextureAndPropagatesTheError()
    {
        using var fixture = new HudNativeComponentFixture();
        using var font = Font(1);
        LoadedTexture? partial = null;
        var failure = new InvalidOperationException("upload failed");
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), false, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface _, bool _, ref LoadedTexture texture) =>
            {
                partial = texture;
                texture.TextureId = 77;
                throw failure;
            }));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            TextDrawing.CreateTexture(fixture.Api.Object, "12", font)));
        Assert.NotNull(partial);
        Assert.True(partial.Disposed);
        fixture.Gui.Verify(gui => gui.DeleteTexture(77), Times.Once);
    }

    [Fact]
    public void InvalidFontIsRejectedBeforeMeasuringOrUploading()
    {
        using var fixture = new HudNativeComponentFixture();
        using var font = Font(1);
        font.GetFontExtents();
        font.StrokeWidth = double.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => TextDrawing.CreateTexture(fixture.Api.Object, "12", font));
        Assert.Equal(Status.Success, CairoFont.FontMeasuringContext.Status);
        Assert.Empty(fixture.Gui.Invocations);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(false, 80, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 80, true)]
    [InlineData(true, 0, true)]
    public void RadialLabelsUseOwnedTextureCreationAndReuseNaturalIntegerDraws(bool progress, int diameter, bool failFirstUpload)
    {
        using var fixture = new HudNativeComponentFixture(1.125f);
        int uploads = 0;
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), false, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                uploads++;
                if (failFirstUpload && uploads == 1) throw new InvalidOperationException("upload failed");
                texture.Width = surface.Width;
                texture.Height = surface.Height;
            }));
        var shader = new Mock<IShaderProgram>();
        fixture.Render.Setup(render => render.GetEngineShader(EnumShaderProgram.Gui)).Returns(shader.Object);
        object renderer = RuntimeHelpers.GetUninitializedObject(progress ? typeof(RadialProgressBarRenderer) : typeof(RadialMenuRenderer));
        SetField(renderer, "capi", fixture.Api.Object);
        LoadedTexture texture;
        if (progress)
        {
            var bar = (RadialProgressBarRenderer)renderer;
            bar.Text = "AutoStashing";
            bar.SetRect(19.37f, 31.63f, 64.5f, 64.5f);
            var draw = renderer.GetType().GetMethod("DrawText", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (failFirstUpload) Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => draw.Invoke(renderer, null)).InnerException);
            for (int frame = 0; frame < 3; frame++) draw.Invoke(renderer, null);
            texture = (LoadedTexture)GetField(renderer, "textTexture");
        }
        else
        {
            var labels = new Dictionary<string, LoadedTexture>();
            SetField(renderer, "labels", labels);
            SetField(renderer, "renderedLabels", new Dictionary<string, string>());
            var draw = renderer.GetType().GetMethod("DrawLabel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (failFirstUpload) Assert.IsType<InvalidOperationException>(
                Assert.Throws<TargetInvocationException>(() => draw.Invoke(renderer, ["test", "Tool mode", 91.37d, 103.63d, diameter, 1d])).InnerException);
            for (int frame = 0; frame < 3; frame++) draw.Invoke(renderer, ["test", "Tool mode", 91.37d, 103.63d, diameter, 1d]);
            texture = labels["test"];
        }
        using (texture)
        using ((CairoFont)GetField(renderer, progress ? "textFont" : "labelFont"))
        {
            Assert.Equal(failFirstUpload ? 2 : 1, uploads);
            var calls = fixture.Render.Invocations.Where(call => call.Method.Name == nameof(IRenderAPI.Render2DLoadedTexture)).ToArray();
            Assert.Equal(3, calls.Length);
            foreach (var call in calls)
            {
                Assert.Same(texture, call.Arguments[0]);
                Assert.Equal(MathF.Round((float)call.Arguments[1]), (float)call.Arguments[1]);
                Assert.Equal(MathF.Round((float)call.Arguments[2]), (float)call.Arguments[2]);
            }
            fixture.Gui.VerifyGet(gui => gui.TextTexture, Times.Never);
        }
    }

    [Fact]
    public void GroupCaptionDoesNotDependOnInheritedTextDrawing()
    {
        using var fixture = new HudNativeComponentFixture(1.125f);
        using var title = new HudOverlayGroupTitleBar(fixture.Api.Object,
            ElementBounds.Fixed(0, 0, 160, 22).WithEmptyParent()) { Text = "Player status", textUtil = null! };
        using var surface = new ImageSurface(Format.Argb32, 200, 40);
        using var context = new Context(surface);
        title.ComposeTextElements(context, surface);
        Assert.Equal(Status.Success, context.Status);
    }

    private static CairoFont Font(double stroke) => CairoFont.WhiteSmallText().WithFontSize(14)
        .WithColor(new double[] { 1, 1, 1, 1 }).WithStroke(new double[] { 0, 0, 0, .85 }, stroke);

    private static void SetField(object value, string name, object field) =>
        value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, field);

    private static object GetField(object value, string name) =>
        value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    private delegate void Upload(ImageSurface surface, bool linear, ref LoadedTexture texture);
}
