using System.Numerics;
using Moq;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators;

/// <summary>Checks registration presentation contracts and independent layer selection without graphics allocation.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorRenderingStyleTests
{
    #region Public API
    #region Registration Contracts
    /// <summary>Existing registrations and selection construction retain the background presentation.</summary>
    [Fact]
    public void Defaults_PreserveBackground()
    {
        var provider = new Mock<IItemSlotIndicatorProvider>();
        var registration = new ItemSlotIndicatorRegistration(provider.Object, 0, 1000, null);
        Assert.Equal(ItemSlotIndicatorRenderingStyle.SlotBackground, registration.ResolveStyle());
        Assert.Equal(new[] { ItemSlotIndicatorRenderingStyle.SlotBackground }, registration.SupportedStyles);
        Assert.Equal(ItemSlotIndicatorRenderingStyle.SlotBackground, new ItemSlotIndicatorRenderSelection(default, null).Style);
    }

    /// <summary>Selector values outside the supported contract fall back to its valid non-background default.</summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    public void Selector_RespectsSupportedStyles(int selected, int expected)
    {
        var registration = new ItemSlotIndicatorRegistration(new Mock<IItemSlotIndicatorProvider>().Object, 0, 1000, null,
            defaultStyle: ItemSlotIndicatorRenderingStyle.SlotOutline,
            supportedStyles: [ItemSlotIndicatorRenderingStyle.SlotOutline, ItemSlotIndicatorRenderingStyle.HorizontalBar],
            styleSelector: () => (ItemSlotIndicatorRenderingStyle)selected);
        Assert.Equal((ItemSlotIndicatorRenderingStyle)expected, registration.ResolveStyle());
    }

    /// <summary>Invalid contracts are rejected and supported-style arrays are snapshotted.</summary>
    [Fact]
    public void Registration_ValidatesAndOwnsMetadata()
    {
        var provider = new Mock<IItemSlotIndicatorProvider>().Object;
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorRegistration(provider, 0, 1000, null,
            defaultStyle: (ItemSlotIndicatorRenderingStyle)99));
        foreach (var styles in new ItemSlotIndicatorRenderingStyle[][] { [], [(ItemSlotIndicatorRenderingStyle)99], [ItemSlotIndicatorRenderingStyle.SlotOutline] })
            Assert.Throws<ArgumentException>(() => new ItemSlotIndicatorRegistration(provider, 0, 1000, null, supportedStyles: styles));
        var supported = new[] { ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.SlotOutline };
        var registration = new ItemSlotIndicatorRegistration(provider, 0, 1000, null, supportedStyles: supported,
            styleSelector: () => ItemSlotIndicatorRenderingStyle.SlotOutline);
        supported[1] = ItemSlotIndicatorRenderingStyle.HorizontalBar;
        Assert.Equal(ItemSlotIndicatorRenderingStyle.SlotOutline, registration.ResolveStyle());
    }

    /// <summary>Primary and overlay selectors remain independent; an overlay-only winner retains its own style.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selection_KeepsLayerStylesIndependent(bool hasPrimary)
    {
        var slot = new ItemSlot(null);
        var indicator = new ItemSlotIndicator(0.5f, Vector4.One);
        var provider = new Mock<IItemSlotIndicatorProvider>();
        provider.Setup(p => p.TryGetIndicator(slot, out indicator)).Returns(true);
        var system = new ItemSlotIndicatorSystem();
        if (hasPrimary) system.Register(provider.Object, defaultStyle: ItemSlotIndicatorRenderingStyle.SlotOutline);
        var style = ItemSlotIndicatorRenderingStyle.HorizontalBar;
        system.Register(provider.Object, overlay: true, supportedStyles:
            [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.HorizontalBar], styleSelector: () => style);
        Assert.True(system.TryGetRenderSelection(slot, out var selected));
        Assert.Equal(hasPrimary ? ItemSlotIndicatorRenderingStyle.SlotOutline : style, selected.Style);
        if (hasPrimary) Assert.Equal(style, selected.OverlayStyle);
        style = ItemSlotIndicatorRenderingStyle.SlotBackground;
        Assert.True(system.TryGetRenderSelection(slot, out selected));
        Assert.Equal(hasPrimary ? ItemSlotIndicatorRenderingStyle.SlotOutline : style, selected.Style);
        if (hasPrimary) Assert.Equal(style, selected.OverlayStyle);
    }
    #endregion

    #region Cached Selection
    /// <summary>Live style changes do not recalculate actual freshness, invalidate its cache, or accelerate its idle schedule.</summary>
    [Fact]
    public void CachedFreshness_ChangesStyleWithoutResamplingOrActivity()
    {
        var world = Mock.Of<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(a => a.World).Returns(world);
        var inventory = new InventoryGeneric(1, "styles", Guid.NewGuid().ToString(), null!)
        {
            Api = api.Object,
            InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>()
        };
        var slot = inventory[0];
        var item = new Mock<MockItem>(1, (byte)0, api.Object);
        var perish = new TransitionState { FreshHours = 100, FreshHoursLeft = 60 };
        item.Setup(i => i.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish)).Returns(perish);
        slot.Itemstack = new ItemStack(item.Object);
        long now = 0;
        var style = ItemSlotIndicatorRenderingStyle.SlotBackground;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        system.Register(new FreshnessIndicatorProvider(), adaptiveSampling: new AdaptiveSamplingOptions(),
            supportedStyles: [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.SlotOutline,
                ItemSlotIndicatorRenderingStyle.HorizontalBar], styleSelector: () => style);
        Assert.True(system.TryGetRenderSelection(slot, out var first));
        Assert.Equal(0.6f, first.Indicator.Fill);
        perish.FreshHoursLeft = 10;
        // Style changes span multiple active intervals but must leave the original one-second timer intact.
        foreach (var time in new[] { 100L, 250L, 500L, 999L })
        {
            now = time;
            style = style == ItemSlotIndicatorRenderingStyle.SlotOutline
                ? ItemSlotIndicatorRenderingStyle.HorizontalBar : ItemSlotIndicatorRenderingStyle.SlotOutline;
            Assert.True(system.TryGetRenderSelection(slot, out var selected));
            Assert.Equal(style, selected.Style);
            Assert.Equal(first.Indicator, selected.Indicator);
        }
        item.Verify(i => i.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish), Times.Once);
        now = 1000;
        Assert.True(system.TryGetRenderSelection(slot, out var refreshed));
        Assert.Equal(0.1f, refreshed.Indicator.Fill);
        item.Verify(i => i.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish), Times.Exactly(2));
    }

    /// <summary>Each cached layer retains all provider data and its own cadence while independently changing styles.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CachedLayers_PreserveSamplesAndApplicability(bool applicable)
    {
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(1)) };
        var primarySample = new ItemSlotIndicator(0.4f, new Vector4(0.1f,0.2f,0.3f,0.5f), new(0.2f,0.8f))
        { ParticlePalette = ItemSlotIndicatorParticlePalette.Default };
        var overlaySample = new ItemSlotIndicator(0.7f, Vector4.One);
        int primaryCalls = 0, overlayCalls = 0;
        var primary = new Mock<IItemSlotIndicatorProvider>();
        primary.Setup(p => p.TryGetIndicator(slot, out primarySample)).Returns(() => { primaryCalls++; return applicable; });
        var overlay = new Mock<IItemSlotIndicatorProvider>();
        overlay.Setup(p => p.TryGetIndicator(slot, out overlaySample)).Returns(() => { overlayCalls++; return true; });
        long now = 0;
        var primaryStyle = ItemSlotIndicatorRenderingStyle.SlotBackground;
        var overlayStyle = ItemSlotIndicatorRenderingStyle.SlotBackground;
        var system = new ItemSlotIndicatorSystem { Clock = () => now };
        system.Register(primary.Object, adaptiveSampling: new AdaptiveSamplingOptions(), supportedStyles:
            [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.SlotOutline], styleSelector: () => primaryStyle);
        system.Register(overlay.Object, overlay: true, adaptiveSampling: new AdaptiveSamplingOptions(), supportedStyles:
            [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.HorizontalBar], styleSelector: () => overlayStyle);
        Assert.True(system.TryGetRenderSelection(slot, out _));
        primaryStyle = ItemSlotIndicatorRenderingStyle.SlotOutline;
        overlayStyle = ItemSlotIndicatorRenderingStyle.HorizontalBar;
        foreach (var time in new[] { 100L, 999L, 1000L, 1100L, 1999L })
        {
            now = time;
            Assert.True(system.TryGetRenderSelection(slot, out var selected));
            Assert.Equal(applicable ? primarySample : overlaySample, selected.Indicator);
            Assert.Equal(applicable ? primaryStyle : overlayStyle, selected.Style);
            if (applicable)
            {
                Assert.Equal(overlaySample, selected.OverlayIndicator);
                Assert.Equal(overlayStyle, selected.OverlayStyle);
            }
            int expectedCalls = time < 1000 ? 1 : 2;
            Assert.Equal(expectedCalls, primaryCalls);
            Assert.Equal(expectedCalls, overlayCalls);
        }
    }
    #endregion
    #endregion
}
