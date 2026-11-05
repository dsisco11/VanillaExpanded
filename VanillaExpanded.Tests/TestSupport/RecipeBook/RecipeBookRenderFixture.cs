using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal sealed class RecipeBookRenderFixture : GuiRenderFixture
{
    internal sealed record Bake(int Id, ItemStack Stack, int Size, float Scale, Action<int> Callback);
    internal readonly RecipeBookIconCache Cache;
    internal readonly List<Mock<IRecipeBookIconShaderProgram>> Programs = new();
    internal readonly List<Mock<MeshRef>> Meshes = new();
    internal readonly List<Bake> Bakes = new();
    internal readonly HashSet<int> Allocations = new();
    internal bool CompleteOnFrame = true;
    internal bool CompileShaders = true;
    internal Action? OnBake;
    private EnumRenderStage stage = EnumRenderStage.Ortho;
    private readonly HashSet<IRenderer> renderers = new();
    private readonly HashSet<Bake> completed = new();
    private readonly TextureAtlasPosition[] positions = new TextureAtlasPosition[4096];

    internal RecipeBookRenderFixture(float scale = 1) : base(scale)
    {
        Cache = new RecipeBookIconCache(Api.Object);
        var atlas = new Mock<IItemTextureAtlasAPI>();
        atlas.SetupGet(value => value.Positions).Returns(positions);
        atlas.Setup(value => value.FreeTextureSpace(It.IsAny<int>())).Callback<int>(id => Assert.True(Allocations.Remove(id)));
        Api.SetupGet(value => value.ItemTextureAtlas).Returns(atlas.Object);
        var shaders = new Mock<IShaderAPI>();
        shaders.Setup(value => value.NewShader(It.IsAny<EnumShaderType>())).Returns(() => Mock.Of<IShader>());
        Api.SetupGet(value => value.Shader).Returns(shaders.Object);
        Events.Setup(value => value.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before, It.IsAny<string>()))
            .Callback<IRenderer, EnumRenderStage, string>((renderer, _, _) => Assert.True(renderers.Add(renderer)));
        Events.Setup(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before))
            .Callback<IRenderer, EnumRenderStage>((renderer, _) => Assert.True(renderers.Remove(renderer)));
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

    public override void Dispose()
    {
        try
        {
            Cache.Dispose();
        }
        finally
        {
            base.Dispose();
        }
    }
}
