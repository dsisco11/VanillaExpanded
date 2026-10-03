using Moq;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.PreparationIndicators;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.PreparationIndicators;

/// <summary>Checks preparation progress, inventory context, and transition lifecycle sampling.</summary>
[Trait("Category", "Unit")]
public sealed class PreparationIndicatorProviderTests
{
    #region Public API
    #region Progress
    /// <summary>Drying and curing use the reported transition fraction and clamp malformed bounds.</summary>
    [Theory]
    [InlineData(EnumTransitionType.Dry, 0, 0)]
    [InlineData(EnumTransitionType.Dry, 0.5f, 0.5f)]
    [InlineData(EnumTransitionType.Dry, 1, 1)]
    [InlineData(EnumTransitionType.Cure, 0, 0)]
    [InlineData(EnumTransitionType.Cure, 0.5f, 0.5f)]
    [InlineData(EnumTransitionType.Cure, 1, 1)]
    [InlineData(EnumTransitionType.Cure, -1, 0)]
    [InlineData(EnumTransitionType.Cure, 2, 1)]
    public void Preparation_UsesReportedLevel(EnumTransitionType type, float level, float expected)
    {
        var (world, api, slot) = CreateContext();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot)).Returns([State(type, level)]);
        slot.Itemstack = new ItemStack(item.Object);
        Assert.True(new PreparationIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(expected, indicator.Fill);
        Assert.Equal(0.5f, indicator.Color.W);
        if (expected == 0 || expected == 1)
        {
            Assert.Equal(IndicatorColorPallette.WithOpacity(IndicatorColorPallette.PreparationColors[expected == 0 ? 0 : 1], 0.5f), indicator.Color);
        }
        item.Verify(value => value.UpdateAndGetTransitionStates(world.Object, slot), Times.Once);
    }

    /// <summary>The real transition implementation advances progress with the actual inventory multiplier.</summary>
    [Theory]
    [InlineData(EnumTransitionType.Dry)]
    [InlineData(EnumTransitionType.Cure)]
    public void NativeTransitions_RespectInventoryRate(EnumTransitionType type)
    {
        var (world, api, slot) = CreateContext();
        world.Setup(value => value.Calendar.TotalHours).Returns(2);
        world.Setup(value => value.Side).Returns(EnumAppSide.Client);
        slot.Inventory.OnAcquireTransitionSpeed += (_, _, _) => 2;
        var item = new MockItem(1, api: api)
        {
            TransitionableProps = [new TransitionableProperties { Type = type }]
        };
        var stack = new ItemStack(item);
        // Seed sampled durations to exercise game advancement without random initial duration generation.
        var transition = new TreeAttribute();
        transition.SetDouble("createdTotalHours", 0);
        transition.SetDouble("lastUpdatedTotalHours", 0);
        transition["freshHours"] = new FloatArrayAttribute([0]);
        transition["transitionHours"] = new FloatArrayAttribute([8]);
        transition["transitionedHours"] = new FloatArrayAttribute([0]);
        stack.Attributes["transitionstate"] = transition;
        slot.Itemstack = stack;
        Assert.True(new PreparationIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(0.5f, indicator.Fill);
        Assert.Equal(4, ((FloatArrayAttribute)transition["transitionedHours"]).value[0]);
    }

    /// <summary>Missing state arrays and unrelated or invalid entries do not create a level.</summary>
    [Fact]
    public void MissingOrInvalidStates_ReturnFalse()
    {
        var (world, api, slot) = CreateContext();
        foreach (TransitionState[]? states in new TransitionState[]?[]
        {
            null, [], [null!], [new TransitionState()], [State(EnumTransitionType.Perish, 0.5f)],
            [State(EnumTransitionType.Ripen, 0.5f)], [State(EnumTransitionType.Dry, float.NaN)],
            [State(EnumTransitionType.Cure, float.PositiveInfinity)]
        })
        {
            var item = new Mock<MockItem>(1, (byte)0, api);
            item.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot)).Returns(states!);
            slot.Itemstack = new ItemStack(item.Object);
            Assert.False(new PreparationIndicatorProvider().TryGetIndicator(slot, out _));
        }
    }

    /// <summary>Scanning continues past unrelated and invalid states to a valid preparation transition.</summary>
    [Fact]
    public void MixedStates_SelectValidPreparation()
    {
        var (world, api, slot) = CreateContext();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot))
            .Returns([State(EnumTransitionType.Perish, 0.8f), State(EnumTransitionType.Dry, float.NaN), State(EnumTransitionType.Cure, 0.3f)]);
        slot.Itemstack = new ItemStack(item.Object);
        Assert.True(new PreparationIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(0.3f, indicator.Fill);
    }

    /// <summary>Empty slots and detached stacks have no usable world context.</summary>
    [Fact]
    public void MissingContext_ReturnsFalse()
    {
        var provider = new PreparationIndicatorProvider();
        var slot = new ItemSlot(null);
        Assert.False(provider.TryGetIndicator(slot, out _));
        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(1));
        Assert.False(provider.TryGetIndicator(slot, out _));
    }
    #endregion

    #region Cache And Lifecycle
    /// <summary>An unchanged stack is sampled again after the short refresh interval expires.</summary>
    [Fact]
    public void ExpiredSample_RefreshesProgress()
    {
        var (world, api, slot) = CreateContext();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.SetupSequence(value => value.UpdateAndGetTransitionStates(world.Object, slot))
            .Returns([State(EnumTransitionType.Dry, 0.2f)])
            .Returns([State(EnumTransitionType.Dry, 0.6f)]);
        slot.Itemstack = new ItemStack(item.Object);
        var provider = new ItemSlotIndicatorSystem { Clock = () => 0 };
        provider.Register(new PreparationIndicatorProvider(), refreshIntervalMilliseconds: 1000);
        Assert.True(provider.TryGetIndicator(slot, out var first));
        Assert.Equal(0.2f, first.Fill);
        provider.Clock = () => 1000;
        Assert.True(provider.TryGetIndicator(slot, out var refreshed));
        Assert.Equal(0.6f, refreshed.Fill);
        item.Verify(value => value.UpdateAndGetTransitionStates(world.Object, slot), Times.Exactly(2));
    }

    /// <summary>Repeated queries reuse a sample, but moving to another inventory immediately samples its world.</summary>
    [Fact]
    public void InventoryChange_RefreshesCachedProgress()
    {
        var (world, api, slot) = CreateContext();
        var (targetWorld, _, target) = CreateContext();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot)).Returns([State(EnumTransitionType.Dry, 0.2f)]);
        item.Setup(value => value.UpdateAndGetTransitionStates(targetWorld.Object, target)).Returns([State(EnumTransitionType.Dry, 0.6f)]);
        slot.Itemstack = new ItemStack(item.Object);
        var provider = new ItemSlotIndicatorSystem();
        provider.Register(new PreparationIndicatorProvider(), refreshIntervalMilliseconds: 1000);
        Assert.True(provider.TryGetIndicator(slot, out var first));
        Assert.True(provider.TryGetIndicator(slot, out var cached));
        Assert.Equal(first, cached);
        item.Verify(value => value.UpdateAndGetTransitionStates(world.Object, slot), Times.Once);
        target.Itemstack = slot.Itemstack;
        slot.Itemstack = null;
        Assert.True(provider.TryGetIndicator(target, out var moved));
        Assert.Equal(0.6f, moved.Fill);
    }

    /// <summary>An in-place collectible replacement immediately invalidates the old sample.</summary>
    [Fact]
    public void CollectibleChange_RefreshesImmediately()
    {
        var (world, api, slot) = CreateContext();
        var original = new Mock<MockItem>(1, (byte)0, api);
        var transformed = new Mock<MockItem>(2, (byte)0, api);
        original.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot)).Returns([State(EnumTransitionType.Dry, 0.8f)]);
        transformed.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot)).Returns([State(EnumTransitionType.Cure, 0.1f)]);
        slot.Itemstack = new ItemStack(original.Object);
        var provider = new ItemSlotIndicatorSystem();
        provider.Register(new PreparationIndicatorProvider(), refreshIntervalMilliseconds: 1000);
        Assert.True(provider.TryGetIndicator(slot, out _));
        slot.Itemstack.SetFrom(new ItemStack(transformed.Object));
        Assert.True(provider.TryGetIndicator(slot, out var current));
        Assert.Equal(0.1f, current.Fill);
    }

    /// <summary>Transitions that remove, replace, or mutate a stack cannot display the old item's state.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TransformationDuringSampling_DiscardsOldProgress(int mutation)
    {
        var (world, api, slot) = CreateContext();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Setup(value => value.UpdateAndGetTransitionStates(world.Object, slot)).Returns(() =>
        {
            // Match the game's removal and in-place replacement cases, plus behavior-owned slot replacement.
            var replacement = new ItemStack(MockItem.CreateNonLightSource(2));
            if (mutation == 0) slot.Itemstack = null;
            else if (mutation == 1) slot.Itemstack = replacement;
            else slot.Itemstack!.SetFrom(replacement);
            return [State(EnumTransitionType.Dry, 1)];
        });
        slot.Itemstack = new ItemStack(item.Object);
        var system = new ItemSlotIndicatorSystem();
        system.Register(new PreparationIndicatorProvider(), refreshIntervalMilliseconds: 1000);
        Assert.False(system.TryGetIndicator(slot, out _));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a transition whose normalized level differs from unrelated lifetime fields.</summary>
    private static TransitionState State(EnumTransitionType type, float level)
    {
        return new TransitionState { Props = new TransitionableProperties { Type = type }, TransitionLevel = level, FreshHours = 100, TransitionedHours = 1 };
    }

    /// <summary>Creates an actual inventory with isolated mocked world and API access.</summary>
    private static (Mock<IWorldAccessor> World, ICoreAPI Api, ItemSlot Slot) CreateContext()
    {
        var world = new Mock<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(value => value.World).Returns(world.Object);
        var inventory = new InventoryGeneric(1, "preparation", Guid.NewGuid().ToString(), null!)
        {
            Api = api.Object,
            InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>()
        };
        return (world, api.Object, inventory[0]);
    }
    #endregion
}

