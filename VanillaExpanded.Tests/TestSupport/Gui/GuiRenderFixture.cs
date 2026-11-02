using Cairo;
using Moq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.TestSupport.Gui;

[CollectionDefinition("Recipe book native GUI", DisableParallelization = true)]
public sealed class CraftingGuiCollection;

internal class GuiRenderFixture : IDisposable
{
    internal readonly Mock<ICoreClientAPI> Api = new();
    internal readonly Mock<IGuiAPI> Gui = new();
    internal readonly Mock<IRenderAPI> Render = new();
    internal readonly Mock<IClientEventAPI> Events = new();
    internal readonly Mock<IShaderProgram> GuiProgram = new();
    internal readonly HashSet<int> Textures = new();
    internal readonly Stack<ElementBounds> Clips = new();
    internal IShaderProgram? Active;
    private readonly float oldScale = RuntimeEnv.GUIScale;
    private readonly int oldThread = RuntimeEnv.MainThreadId;
    private int nextTexture;

    internal GuiRenderFixture(float scale = 1)
    {
        RuntimeEnv.GUIScale = scale;
        RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;
        Api.SetupGet(value => value.Gui).Returns(Gui.Object);
        Api.SetupGet(value => value.Render).Returns(Render.Object);
        Api.SetupGet(value => value.Event).Returns(Events.Object);
        Api.SetupGet(value => value.Input).Returns(Mock.Of<IInputAPI>());
        Api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        Gui.SetupGet(value => value.TextTexture).Returns(new TextTextureUtil(Api.Object));
        Gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(),
            ref It.Ref<LoadedTexture>.IsAny)).Callback(new Upload((ImageSurface image, bool linear, ref LoadedTexture texture) =>
        {
            if (texture.TextureId == 0) { texture.TextureId = ++nextTexture; Textures.Add(texture.TextureId); }
            texture.Width = image.Width;
            texture.Height = image.Height;
        }));
        Gui.Setup(value => value.DeleteTexture(It.IsAny<int>())).Callback<int>(id => Assert.True(Textures.Remove(id)));
        GuiProgram.Setup(value => value.Use()).Callback(() => Active = GuiProgram.Object);
        GuiProgram.Setup(value => value.Stop()).Callback(() => Active = null);
        Active = GuiProgram.Object;
        Render.SetupGet(value => value.CurrentActiveShader).Returns(() => Active!);
        Render.SetupGet(value => value.CurrentProjectionMatrix).Returns(Mat4f.Create());
        Render.SetupGet(value => value.CurrentModelviewMatrix).Returns(Mat4f.Create());
        Render.SetupGet(value => value.ScissorStack).Returns(Clips);
        Render.Setup(value => value.PushScissor(It.IsAny<ElementBounds>(), It.IsAny<bool>()))
            .Callback<ElementBounds, bool>((bounds, _) => Clips.Push(bounds));
        Render.Setup(value => value.PopScissor()).Callback(() => { Assert.NotEmpty(Clips); Clips.Pop(); });
    }

    public virtual void Dispose()
    {
        RuntimeEnv.GUIScale = oldScale;
        RuntimeEnv.MainThreadId = oldThread;
    }

    private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);
}
