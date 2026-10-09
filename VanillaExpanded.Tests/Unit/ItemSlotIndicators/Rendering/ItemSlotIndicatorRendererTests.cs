using System.Numerics;

using Moq;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Rendering;
using VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks selected-provider fallback, failure/reload policy, draw ordering, and immutable shared inputs.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorRendererTests
{
    #region Public API
    #region Bounded Drawing
    /// <summary>Freshness restores before food particles draw using their independent fill.</summary>
    [Fact]
    public void IndependentLayers_DrawFreshnessBeforeFoodAmount()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:food", "test", "vanillaexpanded_itemslot_test");
        context.Resources.Register(effect);
        context.Resources.Initialize();
        var backend = Backend();
        var order = new List<string>();
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.Is<ItemSlotIndicatorDrawInput>(i => i.Fill == 0.25f)))
            .Callback(() => order.Add("freshness"));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(),
            It.Is<ItemSlotIndicatorDrawInput>(i => i.Fill == 0.75f), effect, It.IsAny<ItemSlotIndicatorFrameSnapshot>()))
            .Callback(() => order.Add("food"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        renderer.Render(24, 24, new(new(0.25f, Vector4.One), null)
        {
            OverlayIndicator = new(0.75f, Vector4.One),
            OverlayEffect = effect
        }, default);
        Assert.Equal(new[] { "freshness", "restore", "food", "restore" }, order);
    }

    /// <summary>A layered effect draws its ordinary fill first and never duplicates it when the effect fails.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackgroundEffect_DrawsFillBeforeParticlesWithoutDuplicateFallback(bool fail)
    {
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:layered", "test", "vanillaexpanded_itemslot_test", drawBackground: true);
        context.Resources.Register(effect);
        context.Resources.Initialize();
        var backend = Backend();
        var order = new List<string>();
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()))
            .Callback(() => order.Add("background"));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() =>
            {
                order.Add("particles");
                if (fail) throw new InvalidOperationException("Particle draw failed.");
            });
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        renderer.Render(24, 24, new(new(0.5f, Vector4.One), effect), default);
        Assert.Equal(new[] { "background", "particles", "restore" }, order);
    }

    /// <summary>Empty bounded resources retain geometry and cues through unavailable-effect fallback.</summary>
    [Fact]
    public void BoundedEmpty_FallbackDrawsMappedFillAndBoundaryCue()
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var backend = Backend();
        var effect = new ItemSlotIndicatorEffectDefinition("test:unavailable", "test", "vanillaexpanded_itemslot_missing");
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        renderer.Render(24, 24, new(new(0, Vector4.One, new ItemSlotIndicatorDrawRange(0.2f, 0.8f)), effect), default);
        backend.Verify(b => b.Rectangle(It.IsAny<MeshRef>(), It.Is<ItemSlotIndicatorDrawInput>(i => i.Fill == 0.2f && i.ResourceFill == 0)), Times.Once);
        backend.Verify(b => b.Rectangle(It.IsAny<MeshRef>(), It.Is<ItemSlotIndicatorDrawInput>(i => i.Fill == 1 && i.SlotBounds.W == 1)), Times.Once);
        backend.Verify(b => b.Begin(), Times.Once);
        backend.Verify(b => b.Restore(), Times.Once);
    }

    /// <summary>Boundary cues draw after successful shader handoff and do not replace the effect with a rectangle.</summary>
    [Fact]
    public void BoundedEffect_DrawsCueAfterReturningToGui()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:bounded", "test", "vanillaexpanded_itemslot_test");
        context.Resources.Register(effect);
        context.Resources.Initialize();
        var backend = Backend();
        var order = new List<string>();
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() => order.Add("effect"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>())).Callback(() => order.Add("cue"));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        renderer.Render(24, 24, new(new(1, Vector4.One, new ItemSlotIndicatorDrawRange(0.2f, 0.8f)), effect), default);
        Assert.Equal(new[] { "effect", "restore", "cue", "restore" }, order);
        backend.Verify(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(),
            It.Is<ItemSlotIndicatorDrawInput>(i => i.Fill == 0.8f && i.ResourceFill == 1), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>()), Times.Once);
    }

    #endregion

    #region Default Drawing and Failure Policy
    /// <summary>Effects consume prepared handles and a copied frame snapshot; recurring draws perform no preparation.</summary>
    [Fact]
    public void PreparedEffect_ReceivesSelectedPresentationAndSharedFrameWithoutResourceWork()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:effect", "test", "vanillaexpanded_itemslot_test", parameters: new Vector4(1, 0, 0, 0), needsCameraMotion: true);
        context.Resources.Register(effect);
        context.Resources.Initialize();
        var backend = Backend();
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        var frame = new ItemSlotIndicatorFrameSnapshot(3, new(0.4f, -0.2f));
        var selected = new ItemSlotIndicatorRenderSelection(new(0.25f, new(0.2f, 0.4f, 0.8f, 0.6f)), effect);
        for (int draw = 0; draw < 250; draw++) renderer.Render(50, 60, selected, frame);
        backend.Verify(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(),
            It.Is<ItemSlotIndicatorDrawInput>(input => input.Fill == 0.25f && input.Color == selected.Indicator.Color), effect, frame), Times.Exactly(250));
        backend.Verify(b => b.Restore(), Times.Exactly(250));
        backend.Verify(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()), Times.Never);
        Assert.Equal(1, context.Backend.CompileCalls);
        Assert.Equal(2, context.Backend.UploadedData.Count);
    }

    /// <summary>An exceptional effect restores before rectangle fallback, then remains unavailable until an explicit reload.</summary>
    [Fact]
    public void FailedDraw_RestoresBeforeFallbackAndDoesNotHideOrReplaceSelectedProvider()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:effect", "test", "vanillaexpanded_itemslot_test", parameters: new Vector4(1, 0, 0, 0), needsCameraMotion: true);
        var variant = new ItemSlotIndicatorEffectDefinition("test:variant", effect.ShaderAssetDomain, effect.ShaderName);
        context.Resources.Register(effect);
        context.Resources.Register(variant);
        context.Resources.Initialize();
        var backend = Backend();
        var order = new List<string>();
        backend.Setup(b => b.Begin()).Callback(() => order.Add("begin"));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() => { order.Add("effect"); throw new InvalidOperationException("Draw failed."); });
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>())).Callback(() => order.Add("rectangle"));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        var selected = new ItemSlotIndicatorRenderSelection(new(1, new(0.9f, 0, 0, 0.6f)), effect);
        renderer.Render(50, 60, selected, default);
        Assert.Equal(new[] { "begin", "effect", "restore", "begin", "rectangle", "restore" }, order);
        backend.Verify(b => b.Rectangle(It.IsAny<MeshRef>(), It.Is<ItemSlotIndicatorDrawInput>(input => input.Fill == 1 && input.Color == selected.Indicator.Color)), Times.Once);
        Assert.False(context.Resources.TryGet(effect, out _, out _));
        Assert.True(context.Resources.TryGet(variant, out _, out _));
        for (int draw = 0; draw < 250; draw++) renderer.Render(50, 60, selected, default);
        backend.Verify(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>()), Times.Once);
        Assert.Single(context.Backend.Failures);
        Assert.Equal(1, context.Backend.CompileCalls);
        context.Reload();
        Assert.True(context.Resources.TryGet(effect, out _, out _));
    }

    /// <summary>Absent, queued, and failed effect programs retain exactly the selected ordinary presentation.</summary>
    [Theory]
    [InlineData("plain")]
    [InlineData("queued")]
    [InlineData("compile")]
    public void UnavailableEffect_UsesOrdinaryRectangle(string state)
    {
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:effect", "test", "vanillaexpanded_itemslot_test", parameters: new Vector4(1, 0, 0, 0), needsCameraMotion: true);
        if (state == "compile")
        {
            context.Backend.CompileFailures.Add(effect.ShaderName);
            context.Resources.Register(effect);
        }
        context.Resources.Initialize();
        if (state == "queued") context.Resources.Register(effect);
        var backend = Backend();
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        var selection = new ItemSlotIndicatorRenderSelection(new(0.5f, Vector4.One), state == "plain" ? null : effect);
        renderer.Render(50, 60, selection, default);
        backend.Verify(b => b.Rectangle(It.IsAny<MeshRef>(), It.Is<ItemSlotIndicatorDrawInput>(input => input.Fill == 0.5f && input.Color == Vector4.One)), Times.Once);
        backend.Verify(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), It.IsAny<ItemSlotIndicatorEffectDefinition>(),
            It.IsAny<ItemSlotIndicatorFrameSnapshot>()), Times.Never);
    }

    /// <summary>Unsupported shader invocation and invisible selections skip the indicator without entering a state scope.</summary>
    [Fact]
    public void UnsupportedAndInvisibleDraws_DoNotTouchGraphicsState()
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var backend = Backend();
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        renderer.Render(0, 0, new(new(0, Vector4.One), null), default);
        renderer.Render(0, 0, new(new(1, Vector4.Zero), null), default);
        renderer.Render(double.NaN, 0, new(new(1, Vector4.One), null), default);
        backend.SetupGet(b => b.Supported).Returns(false);
        renderer.Render(0, 0, new(new(1, Vector4.One), null), default);
        backend.Verify(b => b.Begin(), Times.Never);
        renderer.Dispose();
        renderer.Dispose();
        backend.Verify(b => b.Dispose(), Times.Once);
    }

    /// <summary>Rectangle exceptions restore the surrounding state and stay contained inside indicator rendering.</summary>
    [Fact]
    public void RectangleException_RestoresAndReportsWithoutEscapingToItemRendering()
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var backend = Backend();
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>())).Throws(new InvalidOperationException());
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48);
        renderer.Render(50, 60, new(new(0.5f, Vector4.One), null), default);
        backend.Verify(b => b.Restore(), Times.Once);
        backend.Verify(b => b.ReportFailure(It.IsAny<Exception>()), Times.Once);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a supported headless draw boundary.</summary>
    private static Mock<IItemSlotIndicatorDrawBackend> Backend()
    {
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        return backend;
    }
    #endregion
}
