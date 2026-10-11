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
[Collection("HudOverlayGeometry")]
public sealed class ToolHeadPresentationIntegrationTests : IDisposable
{
    private readonly Vintagestory.API.Config.ITranslationService? english = Vintagestory.API.Config.Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    #region Lifecycle
    /// <summary>Supplies deterministic borrowed localization for HUD integration.</summary>
    public ToolHeadPresentationIntegrationTests()
    {
        var translations = new Mock<Vintagestory.API.Config.ITranslationService>();
        translations.Setup(service => service.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        Vintagestory.API.Config.Lang.AvailableLanguages["en"] = translations.Object;
    }
    /// <summary>Restores localization after each isolated HUD test.</summary>
    public void Dispose()
    {
        if (english == null) Vintagestory.API.Config.Lang.AvailableLanguages.Remove("en");
        else Vintagestory.API.Config.Lang.AvailableLanguages["en"] = english;
    }

    #endregion
    #region Dedicated presentation
    /// <summary>Configured draws ignore inventory presentation, prepare once, notify once, and borrow selected resources.</summary>
    [Theory]
    [InlineData(2f)]
    [InlineData(99f)]
    public void DedicatedDrawPreservesPreparedMeshAndIndependentMatrix(float guiScale)
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
        Assert.Equal(40, f.Matrices["modelMatrix"][0]);
        Assert.Equal(80, f.Matrices["modelMatrix"][12]);
        Assert.Equal(220, f.Matrices["modelMatrix"][13]);
        Assert.Equal(1, f.Restores);
    }

    /// <summary>HUD icons reuse authored presentation at the HUD depth or fall back once inside their own intersected clip.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HudRoutingKeepsCountOutsideClippedIcon(bool configured)
    {
        var f = new Fixture();
        if (!configured) f.Item.Attributes = null;
        f.Api.SetupGet(api => api.Gui).Returns(new Mock<IGuiAPI>().Object);
        f.Render.Setup(render => render.GetItemStackRenderInfo(It.IsAny<ItemSlot>(), EnumItemRenderTarget.Gui, 0))
            .Returns(() => { f.Preparations++; return f.Info; });
        var field = typeof(ToolHeadPresentationSystem).GetField("<Renderer>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, f.Renderer);
            using var presentation = new VanillaExpanded.HudOverlays.Rendering.HudOverlayIconTextPresentation(
                (_, _) => new LoadedTexture(f.Api.Object) { Width = 20, Height = 14 }, iconSize: 24,
                textBeforeIcon: true, gap: 4, fontSize: 14, useItemPresentation: true);
            presentation.SetContent(new ItemStack(f.Item), "20x");
            presentation.Prepare(new VanillaExpanded.HudOverlays.Registration.HudOverlayPreparationContext(f.Api.Object, 1, "en"));
            var bounds = ElementBounds.Fixed(0, 0, 48, 24).WithEmptyParent();
            bounds.absFixedX = 10; bounds.absFixedY = 20; bounds.absInnerWidth = 48; bounds.absInnerHeight = 24;
            presentation.Draw(f.Render.Object, bounds, new System.Drawing.RectangleF(40, 22, 16, 20), .1f);
            Assert.Equal(configured ? 1 : 0, f.Draws);
            Assert.Equal(configured ? 1 : 0, f.Preparations);
            f.Render.Verify(render => render.RenderItemstackToGui(It.IsAny<ItemSlot>(), 46, 32, 50, 24, -1, .1f, true, false, false),
                configured ? Times.Never() : Times.Once());
            f.Render.Verify(render => render.Render2DTexturePremultipliedAlpha(0, 10, It.Is<double>(y => Math.Abs(y - 25) < .001), 20, 14, 50), Times.Once());
            f.Render.Verify(render => render.PushScissor(It.Is<ElementBounds>(clip => clip.renderX == 40 && clip.renderY == 22
                && clip.OuterWidth == 16 && clip.OuterHeight == 20), true), Times.Once());
            f.Render.Verify(render => render.PopScissor(), Times.Once());
            if (configured) Assert.Equal(50, f.Matrices["modelMatrix"][2] * .5f + f.Matrices["modelMatrix"][6] * .5f + f.Matrices["modelMatrix"][10] * .5f + f.Matrices["modelMatrix"][14]);
        }
        finally { field.SetValue(null, previous); }
    }

    /// <summary>Dedicated HUD submission failures restore the icon clip and the caller's borrowed shader.</summary>
    [Fact]
    public void HudSubmissionFailureRestoresClipAndShader()
    {
        var f = new Fixture();
        var caller = new Mock<IShaderProgram>();
        f.Api.SetupGet(api => api.Gui).Returns(new Mock<IGuiAPI>().Object);
        f.Render.SetupGet(render => render.CurrentActiveShader).Returns(caller.Object);
        caller.Setup(shader => shader.Stop()).Callback(() => f.Render.SetupGet(render => render.CurrentActiveShader).Returns((IShaderProgram)null!));
        f.Shader.Setup(shader => shader.Use()).Callback(() => f.Render.SetupGet(render => render.CurrentActiveShader).Returns(f.Shader.Object));
        f.Render.Setup(render => render.GetItemStackRenderInfo(It.IsAny<ItemSlot>(), EnumItemRenderTarget.Gui, 0)).Returns(f.Info);
        f.Render.Setup(render => render.RenderMultiTextureMesh(It.IsAny<MultiTextureMeshRef>(), "tex2d", 0)).Throws(new InvalidOperationException("draw"));
        var field = typeof(ToolHeadPresentationSystem).GetField("<Renderer>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        try
        {
            field.SetValue(null, f.Renderer);
            using var presentation = new VanillaExpanded.HudOverlays.Rendering.HudOverlayIconTextPresentation(
                (_, _) => new LoadedTexture(f.Api.Object), iconSize: 24, useItemPresentation: true);
            presentation.SetContent(new ItemStack(f.Item), "20x");
            presentation.Prepare(new VanillaExpanded.HudOverlays.Registration.HudOverlayPreparationContext(f.Api.Object, 1, "en"));
            var bounds = ElementBounds.Fixed(10, 20, 24, 24).WithEmptyParent();
            bounds.CalcWorldBounds();
            Assert.Throws<InvalidOperationException>(() => presentation.Draw(f.Render.Object, bounds, new System.Drawing.RectangleF(0, 0, 100, 100), .1f));
            f.Render.Verify(render => render.PopScissor(), Times.Once());
            caller.Verify(shader => shader.Stop(), Times.Once());
            caller.Verify(shader => shader.Use(), Times.Once());
            f.Shader.Verify(shader => shader.Stop(), Times.Once());
            Assert.Equal(1, f.Restores);
        }
        finally { field.SetValue(null, previous); }
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
    /// <summary>Screen-space lighting remains toward the upper right as the tool presentation rotates.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void DedicatedDrawUsesFixedUpperRightLight(float rotation)
    {
        var f = new Fixture();
        float[]? light = null;
        f.Shader.Setup(shader => shader.Uniform("lightPosition", It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Callback<string, float, float, float>((name, x, y, z) => light = [x, y, z]);
        bool submitted = false;
        f.Render.Setup(render => render.RenderMultiTextureMesh(It.IsAny<MultiTextureMeshRef>(), "tex2d", 0))
            .Callback(() =>
            {
                submitted = true;
                Assert.NotNull(light);
                Assert.Equal([0.9f, -0.9f, 2.7166154f], light);
                var model = f.Matrices["modelMatrix"];
                double length = Math.Sqrt(model[8] * model[8] + model[9] * model[9] + model[10] * model[10]);
                double nx = model[8] / length, ny = model[9] / length, nz = model[10] / length;
                double dot = nx * light[0] + ny * light[1] + nz * light[2];
                // Match the installed GUI shader's diffuse, sky and north-facing terms for opposite face normals.
                double front = Math.Max(Math.Max(.45, .5 + .5 * dot), ny * .95) + Math.Max(0, -nz) * .2;
                double rear = Math.Max(Math.Max(.45, .5 - .5 * dot), -ny * .95) + Math.Max(0, nz) * .2;
                Assert.True(front > rear, "The viewer-facing surface must be brighter than its rear-facing inverse.");
            });
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 100, 200, 40, rotation));
        Assert.True(submitted);
        Assert.Equal(1, f.Restores);
    }

    /// <summary>The dedicated draw retains prepared unshaded material semantics.</summary>
    [Fact]
    public void PreparedUnshadedMaterialRemainsUnshaded()
    {
        var fixture = new Fixture();
        fixture.Info.NormalShaded = false;
        Assert.True(fixture.Renderer.TryRender(fixture.Api.Object, fixture.Slot, 100, 200, 40, 0));
        fixture.Shader.Verify(shader => shader.Uniform("normalShaded", 0), Times.Once);
        fixture.Shader.Verify(shader => shader.Uniform("rgbaIn", It.Is<Vec4f>(value => value.X == 1 && value.Y == 1 && value.Z == 1 && value.W == 1)), Times.Once);
    }

    /// <summary>The stock shader receives half ambient terms with stronger directional light and unchanged opacity.</summary>
    [Theory]
    [InlineData(1d, 0d, 0d)]
    [InlineData(-1d, 0d, 0d)]
    [InlineData(0d, 1d, 0d)]
    [InlineData(0d, -1d, 0d)]
    [InlineData(0d, 0d, 1d)]
    [InlineData(0d, 0d, -1d)]
    public void ShadedDrawHalvesAmbientAndStrengthensDirectLight(double nx, double ny, double nz)
    {
        var fixture = new Fixture();
        float[]? light = null;
        Vec4f? tint = null;
        fixture.Shader.Setup(shader => shader.Uniform("lightPosition", It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Callback<string, float, float, float>((name, x, y, z) => light = [x, y, z]);
        fixture.Shader.Setup(shader => shader.Uniform("rgbaIn", It.IsAny<Vec4f>()))
            .Callback<string, Vec4f>((name, value) => tint = value);
        Assert.True(fixture.Renderer.TryRender(fixture.Api.Object, fixture.Slot, 100, 200, 40, 0));
        Assert.NotNull(light);
        Assert.NotNull(tint);
        Assert.Equal(.5f, tint.X);
        Assert.Equal(.5f, tint.Y);
        Assert.Equal(.5f, tint.Z);
        Assert.Equal(1f, tint.W);
        double shaderDot = nx * light[0] + ny * light[1] + nz * light[2];
        // Evaluate the installed stock equation separately from the requested half-ambient/direct model.
        double actual = tint.X * (Math.Max(Math.Max(.45, .5 + .5 * shaderDot), .95 * ny) + .2 * Math.Max(0, -nz));
        double directDot = nx * .3 - ny * .3 + nz * Math.Sqrt(.82);
        double expected = Math.Max(Math.Max(.225, .25 + .75 * directDot), .475 * ny) + .1 * Math.Max(0, -nz);
        Assert.InRange(Math.Abs(actual - expected), 0, .000001);
        if (nz == 1)
        {
            Assert.True(directDot > 1 / Math.Sqrt(3), "The new light must face the viewer more directly than the old diagonal light.");
            Assert.True(actual > .25 + .5 * directDot, "The directional contribution must exceed the previous strength while ambient remains halved.");
        }
    }

    /// <summary>Shader submissions preserve prepared shading, temperature, damage, and transition overlay settings.</summary>
    [Fact]
    public void PreparedShaderEffectsAndOverlayAreSubmitted()
    {
        var f = new Fixture();
        var overlay = new LoadedTexture(f.Api.Object) { TextureId = 13, Width = 32, Height = 64, IgnoreUndisposed = true };
        f.Info.OverlayTexture = overlay;
        f.Info.OverlayOpacity = 0.7f;
        f.Info.TextureSize.Width = 256;
        f.Info.TextureSize.Height = 512;
        f.Render.Setup(r => r.GetTextureAtlasPosition(f.Slot.Itemstack)).Returns(new TextureAtlasPosition { x1 = 0.25f, y1 = 0.5f });
        f.Slot.Itemstack!.Attributes.SetFloat("temperature",800);
        Assert.True(f.Renderer.TryRender(f.Api.Object, f.Slot, 100, 200, 40, 0));
        f.Shader.Verify(s => s.Uniform("normalShaded",1), Times.Once);
        f.Shader.Verify(s => s.Uniform("applyColor",1), Times.Once);
        f.Shader.Verify(s => s.Uniform("alphaTest",0.05f), Times.Once);
        f.Shader.Verify(s => s.Uniform("extraGlow",125), Times.Once);
        f.Shader.Verify(s => s.Uniform("tempGlowMode",1), Times.Once);
        float[] incandescent = Vintagestory.API.MathTools.ColorUtil.GetIncandescenceColorAsColor4f(800);
        f.Shader.Verify(shader => shader.Uniform("rgbaGlowIn", It.Is<Vec4f>(value =>
            value.X == incandescent[0] * .5f && value.Y == incandescent[1] * .5f && value.Z == incandescent[2] * .5f && value.W == 125 / 255f)), Times.Once);
        f.Shader.Verify(s => s.Uniform("damageEffect",0.4f), Times.Once);
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




