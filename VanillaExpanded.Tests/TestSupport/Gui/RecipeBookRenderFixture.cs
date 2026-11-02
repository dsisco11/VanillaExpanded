using Cairo;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.TestSupport.Gui;

[CollectionDefinition("Recipe book native GUI", DisableParallelization = true)]
public sealed class CraftingGuiCollection;

internal sealed class RecipeBookRenderFixture : IDisposable
{
    internal sealed record Bake(int Id, ItemStack Stack, int Size, float Scale, Action<int> Callback);
    internal readonly Mock<ICoreClientAPI> Api = new();
    internal readonly Mock<IGuiAPI> Gui = new();
    internal readonly Mock<IRenderAPI> Render = new();
    internal readonly Mock<IClientEventAPI> Events = new();
    internal readonly Mock<IShaderProgram> GuiProgram = new();
    internal readonly RecipeBookIconCache Cache;
    internal readonly List<Mock<IRecipeBookIconShaderProgram>> Programs = new();
    internal readonly List<Mock<MeshRef>> Meshes = new();
    internal readonly List<Bake> Bakes = new();
    internal readonly HashSet<int> Allocations = new();
    internal readonly HashSet<int> Textures = new();
    internal readonly Stack<ElementBounds> Clips = new();
    internal IShaderProgram? Active;
    internal bool CompleteOnFrame = true;
    internal bool CompileShaders = true;
    internal Action? OnBake;
    private EnumRenderStage stage = EnumRenderStage.Ortho;
    private readonly HashSet<IRenderer> renderers = new();
    private readonly HashSet<Bake> completed = new();
    private readonly TextureAtlasPosition[] positions = new TextureAtlasPosition[4096];
    private readonly float oldScale = RuntimeEnv.GUIScale;
    private readonly int oldThread = RuntimeEnv.MainThreadId;
    private int nextTexture;

    internal RecipeBookRenderFixture(float scale = 1)
    {
        Cache = new RecipeBookIconCache(Api.Object);
        RuntimeEnv.GUIScale = scale;
        RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;
        var atlas = new Mock<IItemTextureAtlasAPI>();
        atlas.SetupGet(value => value.Positions).Returns(positions);
        atlas.Setup(value => value.FreeTextureSpace(It.IsAny<int>())).Callback<int>(id => Assert.True(Allocations.Remove(id)));
        Api.SetupGet(value => value.ItemTextureAtlas).Returns(atlas.Object);
        Api.SetupGet(value => value.Gui).Returns(Gui.Object);
        Api.SetupGet(value => value.Render).Returns(Render.Object);
        Api.SetupGet(value => value.Event).Returns(Events.Object);
        Events.Setup(value => value.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before, It.IsAny<string>()))
            .Callback<IRenderer, EnumRenderStage, string>((renderer, _, _) => Assert.True(renderers.Add(renderer)));
        Events.Setup(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before))
            .Callback<IRenderer, EnumRenderStage>((renderer, _) => Assert.True(renderers.Remove(renderer)));
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
        var shaders = new Mock<IShaderAPI>();
        shaders.Setup(value => value.NewShader(It.IsAny<EnumShaderType>())).Returns(() => Mock.Of<IShader>());
        Api.SetupGet(value => value.Shader).Returns(shaders.Object);
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
        Render.Setup(value => value.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
        {
            var mesh = new Mock<MeshRef> { CallBase = true };
            Meshes.Add(mesh);
            return mesh.Object;
        });
        Render.Setup(value => value.RenderItemStackToAtlas(It.IsAny<ItemStack>(), It.IsAny<ITextureAtlasAPI>(),
            It.IsAny<int>(), It.IsAny<Action<int>>(), It.IsAny<int>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns((ItemStack stack, ITextureAtlasAPI _, int size, Action<int> callback, int color, float sepia, float scale) =>
            {
                Assert.Equal(EnumRenderStage.Before, stage);
                OnBake?.Invoke();
                var bake = new Bake(Bakes.Count + 1, stack, size, scale, callback);
                Bakes.Add(bake);
                return false;
            });
    }

    internal IRecipeBookIconShaderProgram CreateShader()
    {
        var program = new Mock<IRecipeBookIconShaderProgram>();
        program.Setup(value => value.Compile()).Returns(() => CompileShaders);
        program.Setup(value => value.Use()).Callback(() => Active = program.Object);
        program.Setup(value => value.Stop()).Callback(() => Active = null);
        Programs.Add(program);
        return program.Object;
    }

    internal void Warm(GuiElementRecipeBookTiles tiles, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            int before = Bakes.Count;
            Frame(tiles);
            Assert.InRange(Bakes.Count - before, 0, RecipeBookIconCache.BakesPerFrame);
        }
    }

    internal void BeforeFrame(IRenderer? renderer = null)
    {
        stage = EnumRenderStage.Before;
        if (renderer != null) renderer.OnRenderFrame(0, stage);
        else foreach (var registered in renderers.ToArray()) registered.OnRenderFrame(0, stage);
    }

    internal void EndFrame()
    {
        stage = EnumRenderStage.Ortho;
        if (CompleteOnFrame)
            foreach (var bake in Bakes.Where(bake => !completed.Contains(bake)).ToArray()) Complete(bake);
    }

    internal void Frame(GuiElementRecipeBookTiles tiles)
    {
        BeforeFrame();
        stage = EnumRenderStage.Ortho;
        tiles.RenderInteractiveElements(0);
        EndFrame();
    }

    internal void Complete(Bake bake)
    {
        Assert.True(completed.Add(bake));
        positions[bake.Id] = new TextureAtlasPosition { atlasTextureId = 77, x1 = .1f, y1 = .2f, x2 = .3f, y2 = .4f };
        Allocations.Add(bake.Id);
        bake.Callback(bake.Id);
    }

    internal void ReplaceAtlasPosition(int id) => positions[id] = new TextureAtlasPosition { reloadIteration = 1 };

    private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);

    public void Dispose()
    {
        Cache.Dispose();
        RuntimeEnv.GUIScale = oldScale;
        RuntimeEnv.MainThreadId = oldThread;
    }
}
