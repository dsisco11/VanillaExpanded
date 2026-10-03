using System.Numerics;

using Moq;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.LiquidContainerIndicators;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.LiquidContainerIndicators;

/// <summary>Checks liquid volume through the game's actual container content APIs.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidContainerIndicatorProviderTests
{
    #region Public API
    #region Applicability
    /// <summary>Empty slots and ordinary blocks or items have no liquid level.</summary>
    [Fact]
    public void NonContainer_ReturnsFalse()
    {
        var provider = new LiquidContainerIndicatorProvider();
        var slot = new ItemSlot(null);
        Assert.False(provider.TryGetIndicator(slot, out _));
        slot.Itemstack = new ItemStack(new Block());
        Assert.False(provider.TryGetIndicator(slot, out _));
        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(1));
        Assert.False(provider.TryGetIndicator(slot, out _));
    }

    /// <summary>Invalid capacity is rejected before producing a level.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidCapacity_ReturnsFalse(float capacity)
    {
        Assert.False(new LiquidContainerIndicatorProvider().TryGetIndicator(CreateSlot(100, 100, capacity), out _));
    }

    /// <summary>A zero portion-to-litre divisor cannot leak an infinite level.</summary>
    [Fact]
    public void NonFiniteVolume_ReturnsFalse()
    {
        Assert.False(new LiquidContainerIndicatorProvider().TryGetIndicator(CreateSlot(100, 0, 10), out _));
    }
    #endregion

    #region Volume
    /// <summary>Portion density and vessel capacity determine fill independently of vessel stack size.</summary>
    [Theory]
    [InlineData(250, 100, 10, 0.25f)]
    [InlineData(25, 10, 5, 0.5f)]
    [InlineData(1000, 100, 10, 1)]
    [InlineData(2000, 100, 10, 1)]
    public void Contents_ProduceProportionalBlueFill(int portions, float density, float capacity, float expected)
    {
        var slot = CreateSlot(portions, density, capacity);
        slot.Itemstack!.StackSize = 4;
        Assert.True(new LiquidContainerIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(expected, indicator.Fill);
        Assert.True(indicator.Color.Z > indicator.Color.Y && indicator.Color.Y > indicator.Color.X);
    }

    /// <summary>Empty containers produce zero fill without a red warning.</summary>
    [Fact]
    public void EmptyContainer_ProducesZeroFill()
    {
        var slot = CreateSlot(100, 100, 10);
        var container = (BlockLiquidContainerBase)slot.Itemstack!.Collectible;
        container.SetContent(slot.Itemstack, null!);
        Assert.True(new LiquidContainerIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(0, indicator.Fill);
        Assert.True(indicator.Color.Z > indicator.Color.X);
    }

    /// <summary>Taking liquid and replacing contents update the same stack without waiting for a cache.</summary>
    [Fact]
    public void LiquidTransfers_UpdateImmediately()
    {
        var slot = CreateSlot(1000, 100, 10);
        var container = (BlockLiquidContainerBase)slot.Itemstack!.Collectible;
        var provider = new LiquidContainerIndicatorProvider();
        Assert.True(provider.TryGetIndicator(slot, out var full));
        Assert.Equal(1, full.Fill);

        // Exercise the owning transfer API, then restore its returned portions to this vessel.
        var removed = container.TryTakeContent(slot.Itemstack, 500);
        Assert.True(provider.TryGetIndicator(slot, out var half));
        Assert.Equal(0.5f, half.Fill);
        container.SetContent(slot.Itemstack, null!);
        Assert.True(provider.TryGetIndicator(slot, out var empty));
        Assert.Equal(0, empty.Fill);
        container.SetContent(slot.Itemstack, removed);
        Assert.True(provider.TryGetIndicator(slot, out var restored));
        Assert.Equal(half, restored);
    }
    #endregion

    #region Provider Selection
    /// <summary>An applicable higher-priority provider wins; otherwise liquid fill supplies the fallback.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LowerPriority_PreservesExistingIndicatorOrFallsBack(bool applicable)
    {
        var slot = CreateSlot(250, 100, 10);
        var higherIndicator = new ItemSlotIndicator(0.7f, Vector4.One);
        var higher = new Mock<IItemSlotIndicatorProvider>();
        higher.Setup(provider => provider.TryGetIndicator(slot, out higherIndicator)).Returns(applicable);
        var liquid = new LiquidContainerIndicatorProvider();
        var system = new ItemSlotIndicatorSystem();
        system.Register(liquid, -10);
        system.Register(higher.Object);

        Assert.True(liquid.TryGetIndicator(slot, out var liquidIndicator));
        Assert.True(system.TryGetRenderSelection(slot, out var selected));
        Assert.Equal(applicable ? higherIndicator : liquidIndicator, selected.Indicator);
        Assert.Null(selected.Effect);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Builds real serialized contents with a world that resolves their collectible identity.</summary>
    private static ItemSlot CreateSlot(int portions, float density, float capacity)
    {
        var world = new Mock<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(value => value.World).Returns(world.Object);
        var liquid = new MockItem(1, api: api.Object)
        {
            Code = new AssetLocation("game:waterportion"),
            Attributes = JsonObject.FromJson("{\"waterTightContainerProps\":{\"itemsPerLitre\":" + density.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}")
        };
        world.Setup(value => value.GetItem(1)).Returns(liquid);
        var container = new TestLiquidContainer(api.Object, capacity);
        var stack = new ItemStack(container);
        container.SetContent(stack, new ItemStack(liquid, portions));
        return new ItemSlot(null) { Itemstack = stack };
    }

    /// <summary>Supplies loaded API state and capacity while retaining the game's container implementation.</summary>
    private sealed class TestLiquidContainer : BlockLiquidContainerBase
    {
        #region Public API
        /// <summary>Initializes the minimal state normally provided when the game loads a vessel.</summary>
        public TestLiquidContainer(ICoreAPI coreApi, float capacity)
        {
            api = coreApi;
            capacityLitresFromAttributes = capacity;
        }
        #endregion
    }
    #endregion
}

