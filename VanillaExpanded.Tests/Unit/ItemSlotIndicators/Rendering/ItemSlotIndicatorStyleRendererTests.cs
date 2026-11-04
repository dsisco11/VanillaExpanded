using System.Numerics;
using Moq;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Rendering;
using VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks style dispatch, compatible effect fallback, and independent overlays through the existing draw boundary.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorStyleRendererTests
{
    #region Public API
    #region Drawing and Compatibility
    /// <summary>The selected bar receives the caller's resource fraction; invisible input does not enter a state scope.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.5f)]
    [InlineData(1)]
    public void HorizontalBarRetainsCallerFractionAndSkipsInvisibleInput(float fraction)
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        var bars = new List<ItemSlotIndicatorDrawInput>();
        backend.Setup(b => b.DurabilityBar(It.IsAny<ItemSlotIndicatorDrawInput>(), It.IsAny<float>()))
            .Callback<ItemSlotIndicatorDrawInput, float>((input, _) => bars.Add(input));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 60, () => 1.25f);
        renderer.Render(-5, -10, new(new(fraction, Vector4.One, new(0.2f, 0.8f)), null,
            ItemSlotIndicatorRenderingStyle.HorizontalBar), default);
        Assert.Equal(fraction, Assert.Single(bars).ResourceFill);
        Assert.Null(bars[0].DrawRange);
        backend.Verify(b => b.Begin(), Times.Once);
        backend.Verify(b => b.Restore(), Times.Once);
        renderer.Render(0, 0, new(new(fraction, Vector4.Zero), null,
            ItemSlotIndicatorRenderingStyle.HorizontalBar), default);
        Assert.Single(bars);
        backend.Verify(b => b.Begin(), Times.Once);
        backend.Verify(b => b.Restore(), Times.Once);
    }

    /// <summary>All availability and failure routes retain the chosen style and restore before fallback.</summary>
    [Theory]
    [InlineData("incompatible", 1)]
    [InlineData("queued", 1)]
    [InlineData("compile", 1)]
    [InlineData("fail", 1)]
    [InlineData("incompatible", 2)]
    [InlineData("queued", 2)]
    [InlineData("compile", 2)]
    [InlineData("fail", 2)]
    public void EffectFallback_RetainsSelectedStyle(string state, int selectedStyle)
    {
        var style = (ItemSlotIndicatorRenderingStyle)selectedStyle;
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:styles", "test", "vanillaexpanded_itemslot_test",
            supportedStyles: state == "incompatible" ? null : [style]);
        if (state == "compile") context.Backend.CompileFailures.Add(effect.ShaderName);
        if (state != "queued") context.Resources.Register(effect);
        context.Resources.Initialize();
        if (state == "queued") context.Resources.Register(effect);
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        var order = new List<string>();
        var rectangleSubmissions = new List<ItemSlotIndicatorDrawInput>();
        var barSubmissions = new List<ItemSlotIndicatorDrawInput>();
        backend.Setup(b => b.Begin()).Callback(() => order.Add("begin"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()))
            .Callback<MeshRef, ItemSlotIndicatorDrawInput>((_, input) =>
            {
                order.Add("rectangle");
                rectangleSubmissions.Add(input);
            });
        backend.Setup(b => b.DurabilityBar(It.IsAny<ItemSlotIndicatorDrawInput>(), It.IsAny<float>()))
            .Callback<ItemSlotIndicatorDrawInput, float>((input, _) =>
            {
                order.Add("bar");
                barSubmissions.Add(input);
            });
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() => { order.Add("effect"); throw new InvalidOperationException(); });
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        renderer.Render(24, 24, new(new(0.5f, Vector4.One), effect, style), default);
        Assert.Equal(state == "fail"
            ? new[] { "begin", "effect", "restore", "begin", "restore" }
            : new[] { "begin", "restore" }, order.Where(value => value is "begin" or "effect" or "restore"));
        if (style == ItemSlotIndicatorRenderingStyle.SlotOutline)
        {
            Assert.Contains(rectangleSubmissions, input => input.DrawOverSlotGui);
            Assert.Empty(barSubmissions);
        }
        else
        {
            Assert.Empty(rectangleSubmissions);
            Assert.Contains(barSubmissions, input => input.DrawOverSlotGui);
        }
        int fallbackBegin = order.LastIndexOf("begin");
        int fallbackSubmission = order.FindIndex(value => value is "rectangle" or "bar");
        int fallbackRestore = order.LastIndexOf("restore");
        Assert.True(fallbackBegin < fallbackSubmission && fallbackSubmission < fallbackRestore);
        if (state == "fail")
            Assert.True(order.IndexOf("restore", order.IndexOf("effect")) < fallbackBegin);
        if (state == "incompatible")
        {
            Assert.True(context.Resources.TryGet(effect, out _, out _));
            Assert.Empty(context.Backend.Failures);
        }
    }

    /// <summary>Compatible effects retain their selected ordinary background, including failure without duplicate background.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void CompatibleEffect_DrawsSelectedBackgroundOnce(int selectedStyle, bool fail)
    {
        var style = (ItemSlotIndicatorRenderingStyle)selectedStyle;
        using var context = new IndicatorResourceTestContext();
        var effect = new ItemSlotIndicatorEffectDefinition("test:styles", "test", "vanillaexpanded_itemslot_test",
            drawBackground: true, supportedStyles: [style]);
        context.Resources.Register(effect);
        context.Resources.Initialize();
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        var order = new List<string>();
        var rectangleSubmissions = new List<ItemSlotIndicatorDrawInput>();
        var barSubmissions = new List<ItemSlotIndicatorDrawInput>();
        backend.Setup(b => b.Begin()).Callback(() => order.Add("begin"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()))
            .Callback<MeshRef, ItemSlotIndicatorDrawInput>((_, input) =>
            {
                order.Add("rectangle");
                rectangleSubmissions.Add(input);
            });
        backend.Setup(b => b.DurabilityBar(It.IsAny<ItemSlotIndicatorDrawInput>(), It.IsAny<float>()))
            .Callback<ItemSlotIndicatorDrawInput, float>((input, _) =>
            {
                order.Add("bar");
                barSubmissions.Add(input);
            });
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() => { order.Add("effect"); if (fail) throw new InvalidOperationException(); });
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        renderer.Render(24, 24, new(new(0.5f, Vector4.One, new(0.2f, 0.8f)), effect, style), default);
        Assert.Equal(new[] { "begin", "effect", "restore" },
            order.Where(value => value is "begin" or "effect" or "restore"));
        int effectIndex = order.IndexOf("effect");
        if (style == ItemSlotIndicatorRenderingStyle.SlotOutline)
        {
            Assert.Contains(rectangleSubmissions, input => input.DrawOverSlotGui);
            Assert.Empty(barSubmissions);
        }
        else
        {
            Assert.Empty(rectangleSubmissions);
            Assert.Contains(barSubmissions, input => input.DrawOverSlotGui && input.ResourceFill == 0.5f);
        }
        Assert.Contains(order.Take(effectIndex), value => value is "rectangle" or "bar");
        Assert.DoesNotContain(order.Skip(effectIndex + 1), value => value is "rectangle" or "bar");
    }

    #endregion
    #region Resource Lifetime and Declaration

    /// <summary>Switching freshness presentation preserves prepared handles and independent food particle ordering/frame input.</summary>
    [Fact]
    public void StyleSwitch_PreservesFoodOverlayAndPreparedResources()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = FoodGrainIndicatorEffect.Definition;
        context.Resources.Register(effect);
        context.Resources.Initialize();
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        var order = new List<string>();
        var rectangleSubmissions = new List<ItemSlotIndicatorDrawInput>();
        var barSubmissions = new List<ItemSlotIndicatorDrawInput>();
        var overlaySubmissions = new List<ItemSlotIndicatorDrawInput>();
        backend.Setup(b => b.Begin()).Callback(() => order.Add("begin"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()))
            .Callback<MeshRef, ItemSlotIndicatorDrawInput>((_, input) =>
            {
                order.Add("rectangle");
                rectangleSubmissions.Add(input);
            });
        backend.Setup(b => b.DurabilityBar(It.IsAny<ItemSlotIndicatorDrawInput>(), It.IsAny<float>()))
            .Callback<ItemSlotIndicatorDrawInput, float>((input, _) =>
            {
                order.Add("bar");
                barSubmissions.Add(input);
            });
        var frame = new ItemSlotIndicatorFrameSnapshot(3, new(0.4f, -0.2f));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(),
            It.Is<ItemSlotIndicatorDrawInput>(i => i.ResourceFill == 0.75f), effect, frame))
            .Callback<IShaderProgram, MeshRef, ItemSlotIndicatorDrawInput, ItemSlotIndicatorEffectDefinition,
                ItemSlotIndicatorFrameSnapshot>((_, _, input, _, _) =>
            {
                order.Add("food");
                overlaySubmissions.Add(input);
            });
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        int uploads = context.Backend.UploadedData.Count, compiles = context.Backend.CompileCalls;
        for (int draw = 0; draw < 60; draw++)
        {
            order.Clear();
            rectangleSubmissions.Clear();
            barSubmissions.Clear();
            overlaySubmissions.Clear();
            var style = (ItemSlotIndicatorRenderingStyle)(draw % 3);
            renderer.Render(24, 24, new(new(0.25f, Vector4.One), null, style)
            { OverlayIndicator = new(0.75f, Vector4.One), OverlayEffect = effect }, frame);
            if (style == ItemSlotIndicatorRenderingStyle.HorizontalBar)
            {
                Assert.Empty(rectangleSubmissions);
                Assert.NotEmpty(barSubmissions);
            }
            else
            {
                Assert.NotEmpty(rectangleSubmissions);
                Assert.Empty(barSubmissions);
            }
            var overlay = Assert.Single(overlaySubmissions);
            Assert.Equal(0.75f, overlay.ResourceFill);
            Assert.Equal(Vector4.One, overlay.Color);
            int primaryDispatch = order.FindIndex(value => value is "rectangle" or "bar");
            int primaryRestore = order.IndexOf("restore");
            int overlayBegin = order.LastIndexOf("begin");
            int overlayDispatch = order.IndexOf("food");
            int overlayRestore = order.LastIndexOf("restore");
            Assert.True(order.IndexOf("begin") < primaryDispatch);
            Assert.True(primaryDispatch < primaryRestore);
            Assert.True(primaryRestore < overlayBegin);
            Assert.True(overlayBegin < overlayDispatch);
            Assert.True(overlayDispatch < overlayRestore);
        }
        Assert.Equal(uploads, context.Backend.UploadedData.Count);
        Assert.Equal(compiles, context.Backend.CompileCalls);
    }

    /// <summary>Style declarations are immutable value contracts, validated before resource registration.</summary>
    [Fact]
    public void EffectStyles_ValidateAndPreserveDefinitionEquality()
    {
        var styles = new[] { ItemSlotIndicatorRenderingStyle.SlotOutline, ItemSlotIndicatorRenderingStyle.HorizontalBar };
        var effect = new ItemSlotIndicatorEffectDefinition("test:styles", "test", "vanillaexpanded_itemslot_test", supportedStyles: styles);
        styles[0] = ItemSlotIndicatorRenderingStyle.SlotBackground;
        Assert.True(effect.SupportsStyle(ItemSlotIndicatorRenderingStyle.SlotOutline));
        Assert.False(effect.SupportsStyle(ItemSlotIndicatorRenderingStyle.SlotBackground));
        Assert.False(effect.SupportsStyle((ItemSlotIndicatorRenderingStyle)99));
        Assert.Equal(effect, new ItemSlotIndicatorEffectDefinition("test:styles", "test", "vanillaexpanded_itemslot_test",
            supportedStyles: [ItemSlotIndicatorRenderingStyle.HorizontalBar, ItemSlotIndicatorRenderingStyle.SlotOutline]));
        Assert.Throws<ArgumentException>(() => new ItemSlotIndicatorEffectDefinition("test:styles", "test", "vanillaexpanded_itemslot_test", supportedStyles: []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorEffectDefinition("test:styles", "test", "vanillaexpanded_itemslot_test", supportedStyles: [(ItemSlotIndicatorRenderingStyle)99]));
        Assert.True(FoodGrainIndicatorEffect.Definition.SupportsStyle(ItemSlotIndicatorRenderingStyle.SlotBackground));
        Assert.False(FoodGrainIndicatorEffect.Definition.SupportsStyle(ItemSlotIndicatorRenderingStyle.HorizontalBar));
        Assert.Throws<ArgumentException>(() => effect.ValidateCompatibility(new ItemSlotIndicatorEffectDefinition(effect.Id, "test", effect.ShaderName)));
    }
    #endregion
    #endregion
}
