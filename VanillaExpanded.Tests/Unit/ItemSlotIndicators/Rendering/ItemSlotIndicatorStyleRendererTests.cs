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
    /// <summary>Ordinary styles retain zero tracks/borders and use raw fractions, never background mapping or cues.</summary>
    [Theory]
    [InlineData(1, 0, 4)]
    [InlineData(1, 0.5f, 4)]
    [InlineData(1, 1, 4)]
    [InlineData(2, 0, 1)]
    [InlineData(2, 0.5f, 2)]
    [InlineData(2, 1, 2)]
    public void OrdinaryStyle_DrawsActualFractionWithoutCues(int selectedStyle, float fraction, int count)
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        var drawn = new List<ItemSlotIndicatorDrawInput>();
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()))
            .Callback<MeshRef, ItemSlotIndicatorDrawInput>((_, input) => drawn.Add(input));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 60, () => 1.25f);
        renderer.Render(-5, -10, new(new(fraction, Vector4.One, new(0.2f, 0.8f)), null,
            (ItemSlotIndicatorRenderingStyle)selectedStyle), default);
        Assert.Equal(count, drawn.Count);
        Assert.All(drawn, input => { Assert.True(input.PreserveFractionalPosition); Assert.Null(input.DrawRange); });
        if (selectedStyle == 2)
        {
            Assert.Equal(55, drawn[0].SlotBounds.Z);
            Assert.Equal(5, drawn[0].SlotBounds.W);
            if (fraction > 0) Assert.Equal(55 * fraction, drawn[1].SlotBounds.Z);
        }
        backend.Verify(b => b.Begin(), Times.Once);
        backend.Verify(b => b.Restore(), Times.Once);
        renderer.Render(0, 0, new(new(fraction, Vector4.Zero), null, (ItemSlotIndicatorRenderingStyle)selectedStyle), default);
        Assert.Equal(count, drawn.Count);
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
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>())).Callback(() => order.Add("rectangle"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() => { order.Add("effect"); throw new InvalidOperationException(); });
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        renderer.Render(24, 24, new(new(0.5f, Vector4.One), effect, style), default);
        int rectangles = style == ItemSlotIndicatorRenderingStyle.SlotOutline ? 4 : 2;
        Assert.Equal(rectangles, order.Count(value => value == "rectangle"));
        Assert.Equal("restore", order[^1]);
        if (state == "fail") Assert.Equal(new[] { "effect", "restore" }, order.Take(2));
        else Assert.DoesNotContain("effect", order);
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
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>())).Callback(() => order.Add("rectangle"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>(), effect,
            It.IsAny<ItemSlotIndicatorFrameSnapshot>())).Callback(() => { order.Add("effect"); if (fail) throw new InvalidOperationException(); });
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        renderer.Render(24, 24, new(new(0.5f, Vector4.One, new(0.2f, 0.8f)), effect, style), default);
        int count = selectedStyle == 1 ? 4 : 2;
        Assert.Equal(Enumerable.Repeat("rectangle", count).Concat(new[] { "effect", "restore" }), order);
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
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>())).Callback(() => order.Add("freshness"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        var frame = new ItemSlotIndicatorFrameSnapshot(3, new(0.4f, -0.2f));
        backend.Setup(b => b.Effect(It.IsAny<IShaderProgram>(), It.IsAny<MeshRef>(),
            It.Is<ItemSlotIndicatorDrawInput>(i => i.ResourceFill == 0.75f), effect, frame)).Callback(() => order.Add("food"));
        using var renderer = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        int uploads = context.Backend.UploadedData.Count, compiles = context.Backend.CompileCalls;
        for (int draw = 0; draw < 60; draw++)
        {
            order.Clear();
            var style = (ItemSlotIndicatorRenderingStyle)(draw % 3);
            renderer.Render(24, 24, new(new(0.25f, Vector4.One), null, style)
            { OverlayIndicator = new(0.75f, Vector4.One), OverlayEffect = effect }, frame);
            int count = style == ItemSlotIndicatorRenderingStyle.SlotBackground ? 1 : style == ItemSlotIndicatorRenderingStyle.SlotOutline ? 4 : 2;
            Assert.Equal(Enumerable.Repeat("freshness", count).Concat(new[] { "restore", "food", "restore" }), order);
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
