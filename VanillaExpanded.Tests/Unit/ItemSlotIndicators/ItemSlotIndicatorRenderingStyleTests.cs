using System.Numerics;
using Moq;
using VanillaExpanded.ItemSlotIndicators;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators;

/// <summary>Checks registration presentation contracts and independent layer selection without graphics allocation.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorRenderingStyleTests
{
    #region Public API
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
}
