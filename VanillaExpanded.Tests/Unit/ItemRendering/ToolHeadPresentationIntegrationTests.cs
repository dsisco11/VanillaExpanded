using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using Newtonsoft.Json.Linq;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Checks public preparation/submission, early fallback, and post-preparation cancellation.</summary>
public sealed class ToolHeadPresentationIntegrationTests
{
    #region Public API
    #region Dedicated presentation
    /// <summary>Configured draws prepare once, notify once, borrow the selected mesh, and leave the caller transform untouched.</summary>
    [Theory]
    [InlineData(2f)]
    [InlineData(99f)]
    public void DedicatedDrawBorrowsPreparedMeshAndLeavesCallerTransformUntouched(float guiScale)
    {
        var f = new Fixture();
        f.Item.GuiTransform = ModelTransform.ItemDefaultGui();
        f.Item.GuiTransform.Scale = guiScale;
        var gui = f.Item.GuiTransform.AsMatrix;
        var selected = new MultiTextureMeshRef([], []);
        f.BeforePrepare = () => { f.Info.ModelRef = selected; f.Info.Transform = ModelTransform.ItemDefaultFp(); };
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 100, 200, 40, 90));
        Assert.Equal(1, f.Preparations);
        Assert.Equal(1, f.Item.IdleCalls);
        Assert.Equal(1, f.Draws);
        Assert.Same(selected, f.Submitted);
        Assert.False(selected.Disposed);
        Assert.Equal(gui, f.Item.GuiTransform.AsMatrix);
        Assert.Equal(1, f.Restores);
    }

    #endregion
    #region Fallback and cancellation
    /// <summary>Quick-tool routing uses one direct configured draw or one ordinary category-offset fallback.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuickToolRoutingChoosesOneRenderingPath(bool configured)
    {
        var f = new Fixture();
        if (!configured) f.Item.Attributes = null;
        int ordinary = 0;
        f.Render.Setup(r => r.RenderItemstackToGui(It.IsAny<ItemSlot>(),100,220,100,40,-1,true,false,false)).Callback(() => ordinary++);
        // The icon creates a new detached slot; preparation accepts that exact invocation rather than the fixture slot.
        f.Render.Setup(r => r.GetItemStackRenderInfo(It.IsAny<ItemSlot>(),EnumItemRenderTarget.Gui,0)).Returns(() => { f.Preparations++; return f.Info; });
        var field = typeof(ToolHeadPresentationSystem).GetField("<Renderer>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null,f.Renderer);
            new VanillaExpanded.QuickTools.QuickToolItemIcon(new ItemStack(f.Item),"tool:Pickaxe").Render(f.Api.Object,100,200,40,true,90);
            Assert.Equal(configured ? 1 : 0,f.Draws);
            Assert.Equal(configured ? 0 : 1,ordinary);
            Assert.Equal(configured ? 1 : 0,f.Preparations);
        }
        finally { field.SetValue(null,previous); }
    }
    /// <summary>All unsupported conditions known before preparation allow ordinary fallback with no callback.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("custom")]
    [InlineData("projection")]
    [InlineData("shader")]
    [InlineData("light-uniform")]
    [InlineData("overflow")]
    [InlineData("active-shader")]
    public void EarlyUnsupportedDrawReturnsFalse(string scenario)
    {
        var f = new Fixture();
        switch(scenario)
        {
            case "missing": f.Item.Attributes = null; break;
            case "invalid": f.Item.Attributes = new JsonObject(JObject.Parse("{\"ve-radial-menu-properties\":{\"transform\":{\"scale\":-1}}}")); break;
            case "custom": f.RegisterCustom(); break;
            case "projection": f.Projection[5] = 1; break;
            case "shader": f.Shader.Setup(s => s.HasUniform("alphaTest")).Returns(false); break;
            case "light-uniform": f.Shader.Setup(s => s.HasUniform("lightPosition")).Returns(false); break;
            case "active-shader": f.Render.SetupGet(r => r.CurrentActiveShader).Returns(new Mock<IShaderProgram>().Object); break;
        }
        Assert.False(f.Renderer.TryRender(f.Api.Object, f.Slot, 100, 200, scenario == "overflow" ? float.PositiveInfinity : 40, 0));
        Assert.Equal(0, f.Preparations);
        Assert.Equal(0, f.Draws);
        Assert.Equal(0, f.Restores);
    }

    /// <summary>Preparation invalidation consumes the draw without callback retry and bounds cancellation diagnostics.</summary>
    [Theory]
    [InlineData("custom")]
    [InlineData("projection")]
    [InlineData("stack")]
    [InlineData("model-view-nan")]
    [InlineData("model-view-overflow")]
    [InlineData("shader")]
    public void PostPreparationInvalidationConsumesWithoutRetry(string scenario)
    {
        var f = new Fixture();
        f.BeforePrepare = () =>
        {
            switch(scenario)
            {
                case "custom": f.RegisterCustom(); break;
                case "projection": f.Projection[5] = 1; break;
                case "stack": f.Slot.Itemstack = new ItemStack(new Item()); break;
                case "model-view-nan": f.View[0] = float.NaN; break;
                case "model-view-overflow": f.View[0] = float.MaxValue; break;
                case "shader": f.Shader.SetupGet(s => s.Disposed).Returns(true); break;
            }
        };
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 0, 0, 40, 0));
        Assert.Equal(1, f.Preparations);
        Assert.Equal(1, f.Item.IdleCalls);
        Assert.Equal(0, f.Draws);
        Assert.Equal(1, f.Restores);
    }

    #endregion
    #region Effects and restoration
    /// <summary>The dedicated draw retains prepared unshaded material semantics.</summary>
    [Fact]
    public void PreparedUnshadedMaterialRemainsUnshaded()
    {
        var fixture = new Fixture();
        fixture.Info.NormalShaded = false;
        Assert.True(fixture.Renderer.TryRender(fixture.Api.Object, fixture.Slot, 100, 200, 40, 0));
        fixture.Shader.Verify(shader => shader.Uniform("normalShaded", 0), Times.Once);
    }

    /// <summary>Prepared draws forward caller-owned shader settings without imposing a lighting policy.</summary>
    [Fact]
    public void PreparedDrawForwardsCallerShaderSettings()
    {
        var fixture = new Fixture();
        fixture.Info.ApplyColor = false;
        fixture.Info.AlphaTest = 0.37f;
        fixture.Info.DamageEffect = 0.62f;
        Assert.True(fixture.Renderer.TryRender(fixture.Api.Object, fixture.Slot, 100, 200, 40, 0));
        fixture.Shader.Verify(shader => shader.Uniform("applyColor", 0), Times.Once);
        fixture.Shader.Verify(shader => shader.Uniform("alphaTest", 0.37f), Times.Once);
        fixture.Shader.Verify(shader => shader.Uniform("damageEffect", 0.62f), Times.Once);
    }

    /// <summary>Overlay submissions forward supplied texture metadata/settings without taking ownership.</summary>
    [Fact]
    public void PreparedOverlayForwardsTextureSettingsWithoutTakingOwnership()
    {
        var f = new Fixture();
        var overlay = new LoadedTexture(f.Api.Object) { TextureId = 13, Width = 32, Height = 64, IgnoreUndisposed = true };
        f.Info.OverlayTexture = overlay;
        f.Info.OverlayOpacity = 0.7f;
        f.Info.TextureSize.Width = 256;
        f.Info.TextureSize.Height = 512;
        f.Render.Setup(r => r.GetTextureAtlasPosition(f.Slot.Itemstack)).Returns(new TextureAtlasPosition { x1 = 0.25f, y1 = 0.5f });
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 100, 200, 40, 0));
        f.Shader.Verify(s => s.Uniform("overlayOpacity",0.7f), Times.Once);
        f.Shader.Verify(s => s.BindTexture2D("tex2dOverlay",13,1), Times.Once);
        f.Shader.Verify(s => s.Uniform("overlayTextureSize",32f,64f), Times.Once);
        f.Shader.Verify(s => s.Uniform("baseTextureSize",256f,512f), Times.Once);
        f.Shader.Verify(s => s.Uniform("baseUvOrigin",0.25f,0.5f), Times.Once);
        Assert.False(overlay.Disposed);
    }

    /// <summary>Item-owned lifecycle invalidation is consumed and restores the caller without submission.</summary>
    [Fact]
    public void IdleAndSubmissionFailuresRestoreBoundary()
    {
        var f = new Fixture();
        f.Item.OnIdle = () => f.RegisterCustom();
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 0, 0, 40, 0));
        Assert.Equal(0,f.Draws);
        Assert.Equal(1,f.Restores);
        var failing = new Fixture();
        failing.Render.Setup(r => r.RenderMultiTextureMesh(It.IsAny<MultiTextureMeshRef>(),"tex2d",0)).Throws(new InvalidOperationException());
        Assert.Throws<InvalidOperationException>(() => failing.Renderer.TryRender(failing.Api.Object,failing.Slot,0,0,40,0));
        Assert.Equal(1,failing.Preparations);
        Assert.Equal(1,failing.Item.IdleCalls);
        Assert.Equal(1,failing.Restores);
    }
    /// <summary>Null prepared mesh consumes once without lifecycle notification or drawing.</summary>
    [Fact]
    public void NullMeshSkipsIdleAndSubmission()
    {
        var f = new Fixture();
        f.Info.ModelRef = null;
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 0, 0, 40, 0));
        Assert.Equal(1, f.Preparations);
        Assert.Equal(0, f.Item.IdleCalls);
        Assert.Equal(0, f.Draws);
        Assert.Equal(1, f.Restores);
    }

    /// <summary>Preparation failures restore the graphics boundary without drawing or retry.</summary>
    [Fact]
    public void PreparationExceptionRestoresBoundary()
    {
        var f = new Fixture();
        f.BeforePrepare = () => throw new InvalidOperationException("prepare");
        Assert.Throws<InvalidOperationException>(() => f.Renderer.TryRender(f.Api.Object, f.Slot, 0, 0, 40, 0));
        Assert.Equal(1, f.Preparations);
        Assert.Equal(1, f.Restores);
        Assert.Equal(0, f.Draws);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Counts lifecycle notifications with a deterministic temperature source.</summary>
    private sealed class TestItem : Item
    {
        public int IdleCalls;
        public Action? OnIdle;
        #region Public API
        /// <summary>Records the separate GUI lifecycle notification.</summary>
        public override void InGuiIdle(IWorldAccessor world, ItemStack stack) { IdleCalls++; OnIdle?.Invoke(); }
        /// <summary>Supplies a hot item for shader effect coverage without world services.</summary>
        public override float GetTemperature(IWorldAccessor world, ItemStack stack) => 800;
        #endregion
    }
    /// <summary>Restores a counted headless state boundary.</summary>
    private sealed class Restore(Action restore) : IDisposable
    {
        #region Public API
        /// <summary>Invokes the fixture's captured restoration.</summary>
        public void Dispose() => restore();
        #endregion
    }
    /// <summary>Supplies public API mocks and the installed live event registry.</summary>
    private sealed class Fixture
    {
        public readonly Mock<ICoreClientAPI> Api = new();
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IShaderProgram> Shader = new();
        public readonly TestItem Item = new() { ItemId = 7, Attributes = new JsonObject(JObject.Parse("{\"ve-radial-menu-properties\":{}}")) };
        public readonly ItemRenderInfo Info = new() { ModelRef = new MultiTextureMeshRef([], []), NormalShaded = true, ApplyColor = true, AlphaTest = 0.05f, DamageEffect = 0.4f };
        public readonly Dictionary<string, float[]> Matrices = new();
        public readonly float[] Projection = new Matrixf().Values;
        public readonly float[] View = new Matrixf().Values;
        public readonly DummySlot Slot;
        public readonly ToolHeadPresentationRenderer Renderer;
        public Action? BeforePrepare;
        public int Preparations, Draws, Restores;
        public MultiTextureMeshRef? Submitted;
        private readonly Dictionary<int, ItemRenderDelegate>[][] registrations;
        #region Public API
        /// <summary>Connects public preparation, shader, mesh submission, and headless capture seams.</summary>
        public Fixture()
        {
            Slot = new DummySlot(new ItemStack(Item));
            Projection[5] = -1;
            Api.SetupGet(a => a.Render).Returns(Render.Object);
            Api.SetupGet(a => a.Logger).Returns(new Mock<ILogger>().Object);
            Render.SetupGet(r => r.CurrentProjectionMatrix).Returns(Projection);
            Render.SetupGet(r => r.CurrentModelviewMatrix).Returns(View);
            Render.SetupGet(r => r.CurrentActiveShader).Returns(Shader.Object);
            Render.Setup(r => r.GetEngineShader(EnumShaderProgram.Gui)).Returns(Shader.Object);
            Shader.Setup(s => s.HasUniform(It.IsAny<string>())).Returns(true);
            Shader.Setup(s => s.UniformMatrix(It.IsAny<string>(), It.IsAny<float[]>())).Callback<string,float[]>((name,m) => Matrices[name] = (float[])m.Clone());
            Render.Setup(r => r.GetItemStackRenderInfo(Slot, EnumItemRenderTarget.Gui, 0)).Returns(() => { Preparations++; BeforePrepare?.Invoke(); return Info; });
            Render.Setup(r => r.RenderMultiTextureMesh(It.IsAny<MultiTextureMeshRef>(), "tex2d", 0)).Callback<MultiTextureMeshRef,string,int>((mesh, _, _) => { Draws++; Submitted = mesh; });
            var eventType = typeof(Vintagestory.Client.NoObf.InventoryItemRenderer).Assembly.GetType("Vintagestory.Client.NoObf.ClientEventAPI")!;
            var events = (IClientEventAPI)RuntimeHelpers.GetUninitializedObject(eventType);
            registrations = Enumerable.Range(0,2).Select(_ => Enumerable.Range(0,8).Select(_ => new Dictionary<int,ItemRenderDelegate>()).ToArray()).ToArray();
            AccessTools.Field(eventType,"itemStackRenderersByTarget").SetValue(events, registrations);
            Api.SetupGet(a => a.Event).Returns(events);
            Renderer = new ToolHeadPresentationRenderer(Api.Object, _ => new Restore(() => Restores++));
        }
        /// <summary>Adds a live unsupported custom GUI delegate.</summary>
        public void RegisterCustom() => registrations[(int)EnumItemClass.Item][(int)EnumItemRenderTarget.Gui][7] = (_,_,_,_,_,_,_,_,_,_) => { };
        #endregion
    }
    #endregion
}


