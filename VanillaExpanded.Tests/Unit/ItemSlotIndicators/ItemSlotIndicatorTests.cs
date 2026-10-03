using System.Numerics;

using Moq;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators;

/// <summary>Checks selection and pure rendering calculations without a graphics context.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorTests
{
    #region Public API
    #region Lifecycle
    /// <summary>The indicator system is loaded only on the client.</summary>
    [Theory]
    [InlineData(EnumAppSide.Client, true)]
    [InlineData(EnumAppSide.Server, false)]
    public void ShouldLoad_OnlyClient(EnumAppSide side, bool expected)
    {
        Assert.Equal(expected, new ItemSlotIndicatorSystem().ShouldLoad(side));
    }

    /// <summary>Disposal also clears providers when client startup has not occurred.</summary>
    [Fact]
    public void Dispose_RemovesProviders()
    {
        var slot = new ItemSlot(null);
        var system = new ItemSlotIndicatorSystem();
        system.Register(CreateProvider(slot, true, default));

        system.Dispose();

        Assert.False(system.TryGetRenderSelection(slot, out _));
    }

    #endregion

    #region Provider Selection
    /// <summary>An empty registry leaves the item unadorned.</summary>
    [Fact]
    public void NoProviders_ReturnsFalse()
    {
        Assert.False(new ItemSlotIndicatorSystem().TryGetRenderSelection(new ItemSlot(null), out var indicator));
        Assert.Equal(default, indicator);
    }

    /// <summary>Inapplicable providers are skipped and zero fill remains a valid result.</summary>
    [Fact]
    public void Selection_SkipsInapplicableProviders_AndAcceptsZeroFill()
    {
        var slot = new ItemSlot(null);
        var expected = new ItemSlotIndicator(0, Vector4.One);
        var system = new ItemSlotIndicatorSystem();
        system.Register(CreateProvider(slot, true, expected));
        system.Register(CreateProvider(slot, false, default), priority: 10);

        Assert.True(system.TryGetRenderSelection(slot, out var actual));
        Assert.Equal(expected, actual.Indicator);
        Assert.Null(actual.Effect);
    }

    /// <summary>Higher priority wins regardless of registration order.</summary>
    [Fact]
    public void Selection_UsesHighestPriority()
    {
        var slot = new ItemSlot(null);
        var expected = new ItemSlotIndicator(0.5f, Vector4.One);
        var system = new ItemSlotIndicatorSystem();
        system.Register(CreateProvider(slot, true, new ItemSlotIndicator(1, Vector4.Zero)));
        system.Register(CreateProvider(slot, true, expected), priority: 10);

        Assert.True(system.TryGetRenderSelection(slot, out var actual));
        Assert.Equal(expected, actual.Indicator);
    }

    /// <summary>Equal priorities select the first registered applicable provider.</summary>
    [Fact]
    public void Selection_EqualPriorities_RetainsRegistrationOrder()
    {
        var slot = new ItemSlot(null);
        var expected = new ItemSlotIndicator(0.5f, Vector4.One);
        var system = new ItemSlotIndicatorSystem();
        system.Register(CreateProvider(slot, true, expected));
        system.Register(CreateProvider(slot, true, default));

        Assert.True(system.TryGetRenderSelection(slot, out var actual));
        Assert.Equal(expected, actual.Indicator);
    }

    /// <summary>A registry with only inapplicable providers returns no indicator.</summary>
    [Fact]
    public void Selection_NoApplicableProvider_ReturnsFalse()
    {
        var slot = new ItemSlot(null);
        var system = new ItemSlotIndicatorSystem();
        system.Register(CreateProvider(slot, false, new ItemSlotIndicator(1, Vector4.One)));

        Assert.False(system.TryGetRenderSelection(slot, out var actual));
        Assert.Equal(default, actual);
    }

    /// <summary>Shutdown removes all providers.</summary>
    [Fact]
    public void Clear_RemovesProviders()
    {
        var slot = new ItemSlot(null);
        var system = new ItemSlotIndicatorSystem();
        system.Register(CreateProvider(slot, true, default));
        system.Clear();

        Assert.False(system.TryGetRenderSelection(slot, out _));
    }
    #endregion

    #region Rendering
    /// <summary>Fill is clamped and anchored to the slot bottom.</summary>
    [Theory]
    [InlineData(-1, 70, 0)]
    [InlineData(0, 70, 0)]
    [InlineData(0.5f, 50, 20)]
    [InlineData(1, 30, 40)]
    [InlineData(2, 30, 40)]
    [InlineData(float.NaN, 70, 0)]
    public void CalculateBounds_BottomAlignedAndClamped(float fill, float expectedY, float expectedHeight)
    {
        var bounds = ItemSlotIndicatorRenderer.CalculateBounds(100, 50, 40, fill);

        Assert.Equal(80, bounds.X);
        Assert.Equal(expectedY, bounds.Y);
        Assert.Equal(40, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
    }

    /// <summary>Only RGB channels are multiplied by alpha.</summary>
    [Fact]
    public void PremultiplyColor_PreservesAlpha()
    {
        var color = ItemSlotIndicatorRenderer.PremultiplyColor(new Vector4(0.8f, 0.4f, 0.2f, 0.5f));

        Assert.Equal(0.4f, color.R);
        Assert.Equal(0.2f, color.G);
        Assert.Equal(0.1f, color.B);
        Assert.Equal(0.5f, color.A);
    }
    #endregion

    #region Freshness
    /// <summary>Freshness does not decorate empty slots or stacks without inventory API access.</summary>
    [Fact]
    public void Freshness_EmptyOrDetachedSlot_ReturnsFalse()
    {
        var provider = new ItemSlotIndicatorSystem();
        provider.Register(new FreshnessIndicatorProvider(), refreshIntervalMilliseconds: 1000);
        var slot = new ItemSlot(null);
        Assert.False(provider.TryGetRenderSelection(slot, out _));

        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(id: 1));
        Assert.False(provider.TryGetRenderSelection(slot, out _));
    }

    /// <summary>Perish states produce indicators, while absent states remain unadorned and cached.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 0.5f)]
    [InlineData(true, 1)]
    public void Freshness_PerishStateDeterminesApplicability_AndCachesResult(bool perishable, float freshness)
    {
        var world = new Mock<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(instance => instance.World).Returns(world.Object);
        var inventory = CreateInventory(api.Object, "first");
        var slot = inventory[0];
        var item = new Mock<MockItem>(1, (byte)0, api.Object);
        TransitionState? state = perishable
            ? new TransitionState { FreshHours = 100, FreshHoursLeft = 100 * freshness }
            : null;
        item.Setup(instance => instance.UpdateAndGetTransitionState(world.Object, slot, EnumTransitionType.Perish))
            .Returns(state!);
        slot.Itemstack = new ItemStack(item.Object);
        var provider = new ItemSlotIndicatorSystem();
        provider.Register(new FreshnessIndicatorProvider(), refreshIntervalMilliseconds: 1000);

        Assert.Equal(perishable, provider.TryGetRenderSelection(slot, out var first));
        Assert.Equal(perishable, provider.TryGetRenderSelection(slot, out var cached));
        Assert.Equal(first, cached);
        if (perishable)
        {
            Assert.Equal(freshness, first.Indicator.Fill);
            Assert.Equal(FreshnessIndicatorProvider.FreshnessColor(freshness), first.Indicator.Color);
        }
        item.Verify(instance => instance.UpdateAndGetTransitionState(world.Object, slot, EnumTransitionType.Perish), Times.Once);
    }

    /// <summary>Moving a cached stack to another inventory forces a new transition lookup.</summary>
    [Fact]
    public void Freshness_InventoryChanged_RefreshesImmediately()
    {
        var world = new Mock<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(instance => instance.World).Returns(world.Object);
        var source = CreateInventory(api.Object, "source");
        var target = CreateInventory(api.Object, "target");
        var item = new Mock<MockItem>(1, (byte)0, api.Object);
        item.Setup(instance => instance.UpdateAndGetTransitionState(world.Object, source[0], EnumTransitionType.Perish))
            .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 100 });
        item.Setup(instance => instance.UpdateAndGetTransitionState(world.Object, target[0], EnumTransitionType.Perish))
            .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
        source[0].Itemstack = new ItemStack(item.Object);
        var provider = new ItemSlotIndicatorSystem();
        provider.Register(new FreshnessIndicatorProvider(), refreshIntervalMilliseconds: 1000);

        Assert.True(provider.TryGetRenderSelection(source[0], out var original));
        target[0].Itemstack = source[0].Itemstack;
        source[0].Itemstack = null;
        Assert.True(provider.TryGetRenderSelection(target[0], out var moved));

        Assert.Equal(1, original.Indicator.Fill);
        Assert.Equal(0.5f, moved.Indicator.Fill);
        item.Verify(instance => instance.UpdateAndGetTransitionState(world.Object, target[0], EnumTransitionType.Perish), Times.Once);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a real inventory with mock API and network access.</summary>
    private static InventoryGeneric CreateInventory(ICoreAPI api, string id)
    {
        var inventory = new InventoryGeneric(1, "indicator", id, null!);
        inventory.Api = api;
        inventory.InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>();
        return inventory;
    }

    /// <summary>Creates a provider with a fixed result for the requested slot.</summary>
    private static IItemSlotIndicatorProvider CreateProvider(ItemSlot slot, bool applicable, ItemSlotIndicator indicator)
    {
        var provider = new Mock<IItemSlotIndicatorProvider>(MockBehavior.Strict);
        provider.Setup(instance => instance.TryGetIndicator(slot, out indicator)).Returns(applicable);
        return provider.Object;
    }
    #endregion
}
