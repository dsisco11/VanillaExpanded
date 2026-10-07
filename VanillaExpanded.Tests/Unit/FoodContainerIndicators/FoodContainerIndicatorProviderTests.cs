using Moq;
using VanillaExpanded.FoodContainerIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.FoodContainerIndicators;

/// <summary>Checks exclusive container routing and preservation of contained freshness.</summary>
[Trait("Category", "Unit")]
public sealed class FoodContainerIndicatorProviderTests : System.IDisposable
{
    private readonly bool previousFoodEffect = VanillaExpandedModSystem.Config.EnableFoodGrainEffect;

    #region Public API
    /// <summary>Enables the optional effect explicitly for provider capability checks.</summary>
    public FoodContainerIndicatorProviderTests()
    {
        VanillaExpandedModSystem.Config.EnableFoodGrainEffect = true;
    }

    /// <summary>Restores the caller's effect setting after each test.</summary>
    public void Dispose()
    {
        VanillaExpandedModSystem.Config.EnableFoodGrainEffect = previousFoodEffect;
    }

    /// <summary>Disabling grains hides only the amount layer, leaving meal freshness applicable.</summary>
    [Fact]
    public void FoodEffectToggle_DoesNotHideFreshness()
    {
        var config = VanillaExpandedModSystem.Config;
        bool previous = config.EnableFoodGrainEffect;
        try
        {
            var (world, api, inventory) = CreateInventory();
            var item = new Mock<MockItem>(1, (byte)0, api);
            item.Object.Attributes = JsonObject.FromJson("{\"mealContainer\":true}");
            item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(CreateMeal(world));
            item.Setup(value => value.UpdateAndGetTransitionState(world, inventory[0], EnumTransitionType.Perish))
                .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
            inventory[0].Itemstack = new ItemStack(item.Object);
            config.EnableFoodGrainEffect = false;
            Assert.False(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out _));
            Assert.True(new FreshnessIndicatorProvider().TryGetIndicator(inventory[0], out _));
            config.EnableFoodGrainEffect = true;
            Assert.True(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out _));
        }
        finally { config.EnableFoodGrainEffect = previous; }
    }

    /// <summary>Food level follows servings and vessel capacity even when no perish state exists.</summary>
    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(1, true, 0.25f)]
    [InlineData(3, true, 0.75f)]
    [InlineData(5, true, 1)]
    public void FoodLevel_UsesServingCapacity(float servings, bool visible, float fill)
    {
        var (world, api, inventory) = CreateInventory();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Object.Attributes = JsonObject.FromJson("{\"mealContainer\":true,\"servingCapacity\":4}");
        var meal = new Mock<IBlockMealContainer>();
        meal.Setup(value => value.GetQuantityServings(world, It.IsAny<ItemStack>())).Returns(servings);
        item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(meal.Object);
        inventory[0].Itemstack = new ItemStack(item.Object);
        Assert.Equal(visible, new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out var indicator));
        if (visible) Assert.Equal(fill, indicator.Fill);
    }

    /// <summary>Actual pie inheritance does not route its direct freshness into the vessel effect.</summary>
    [Fact]
    public void Pie_UsesOrdinaryFreshnessDespiteMealInterface()
    {
        var (world, _, inventory) = CreateInventory();
        var pie = new Mock<BlockPie> { CallBase = true };
        pie.Setup(value => value.UpdateAndGetTransitionState(world, inventory[0], EnumTransitionType.Perish))
            .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
        inventory[0].Itemstack = new ItemStack(pie.Object);
        Assert.NotNull(pie.Object.GetCollectibleInterface<IBlockMealContainer>());
        Assert.False(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out _));
        Assert.True(new FreshnessIndicatorProvider().TryGetIndicator(inventory[0], out var indicator));
        Assert.Equal(0.5f, indicator.Fill);
        Assert.Null(indicator.DrawRange);
        Assert.Null(indicator.ParticlePalette);
    }

    /// <summary>Each game's vessel metadata route accepts meal containers while rejecting metadata on ordinary food.</summary>
    [Theory]
    [InlineData("{\"mealContainer\":true}")]
    [InlineData("{\"eatenBlock\":\"game:bowl-fired\"}")]
    [InlineData("{\"emptiedBlockCode\":\"game:pot-fired\"}")]
    public void VesselMetadata_RequiresMealHandling(string metadata)
    {
        var item = new Mock<MockItem>(1, (byte)0, Mock.Of<ICoreAPI>());
        item.Object.Attributes = JsonObject.FromJson(metadata);
        Assert.False(FoodContainerClassification.IsFoodContainer(item.Object));
        item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(Mock.Of<IBlockMealContainer>());
        Assert.True(FoodContainerClassification.IsFoodContainer(item.Object));
    }

    /// <summary>Freshness remains available on vessels alongside an independently sampled serving level.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshnessAndServingLevel_AreIndependent(bool foodContainer)
    {
        var (world, api, inventory) = CreateInventory();
        var slot = inventory[0];
        var item = new Mock<MockItem>(1, (byte)0, api);
        if (foodContainer) item.Object.Attributes = JsonObject.FromJson("{\"mealContainer\":true}");
        item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>())
            .Returns(foodContainer ? CreateMeal(world) : null!);
        item.Setup(value => value.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish))
            .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
        slot.Itemstack = new ItemStack(item.Object);

        Assert.True(new FreshnessIndicatorProvider().TryGetIndicator(slot, out var ordinary));
        Assert.Equal(foodContainer, new FoodContainerIndicatorProvider().TryGetIndicator(slot, out var contained));
        var actual = foodContainer ? contained : ordinary;
        Assert.Equal(0.5f, actual.Fill);
        Assert.Equal(FreshnessIndicatorProvider.FreshnessColor(0.5f), ordinary.Color);
        item.Verify(value => value.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish), Times.Once);
    }

    /// <summary>Empty food stays hidden; nonperishable food still reports servings and freshness retains inventory rates.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Contents_RequirePerishStateAndUseInventoryRates(bool hasContents, bool perishable)
    {
        var (world, api, inventory) = CreateInventory();
        var slot = inventory[0];
        var container = new Mock<MockItem>(1, (byte)0, api);
        container.Object.Attributes = JsonObject.FromJson("{\"mealContainer\":true}");
        var meal = new Mock<IBlockMealContainer>();
        container.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(meal.Object);
        meal.Setup(value => value.GetQuantityServings(world, It.IsAny<ItemStack>())).Returns(hasContents ? 1 : 0);
        slot.Itemstack = new ItemStack(container.Object);
        var content = new Mock<MockItem>(2, (byte)0, api);
        var contentStack = new ItemStack(content.Object);
        meal.Setup(value => value.GetNonEmptyContents(world, slot.Itemstack))
            .Returns(hasContents ? [contentStack] : []);
        content.Setup(value => value.GetTransitionableProperties(world, contentStack, null))
            .Returns(perishable ? [new TransitionableProperties { Type = EnumTransitionType.Perish }] : []);
        inventory.OnAcquireTransitionSpeed += (_, _, _) => 0.25f;
        content.Setup(value => value.UpdateAndGetTransitionState(world, It.IsAny<ItemSlot>(), EnumTransitionType.Perish))
            .Returns((IWorldAccessor _, ItemSlot contentSlot, EnumTransitionType type) =>
            {
                Assert.Same(contentStack, contentSlot.Itemstack);
                Assert.Equal(inventory.GetTransitionSpeedMul(type, contentStack),
                    contentSlot.Inventory.GetTransitionSpeedMul(type, contentStack));
                return new TransitionState { FreshHours = 100, FreshHoursLeft = 25 };
            });

        Assert.Equal(hasContents && perishable, new FreshnessIndicatorProvider().TryGetIndicator(slot, out var freshness));
        Assert.Equal(hasContents, new FoodContainerIndicatorProvider().TryGetIndicator(slot, out var indicator));
        if (hasContents) Assert.Equal(1, indicator.Fill);
        if (hasContents && perishable) Assert.Equal(0.25f, freshness.Fill);
    }
    #endregion

    #region Private
    /// <summary>Supplies a half-full single-serving vessel independently of its freshness.</summary>
    private static IBlockMealContainer CreateMeal(IWorldAccessor world)
    {
        var meal = new Mock<IBlockMealContainer>();
        meal.Setup(value => value.GetQuantityServings(world, It.IsAny<ItemStack>())).Returns(0.5f);
        return meal.Object;
    }

    /// <summary>Creates an attached inventory for transition-rate forwarding without graphics.</summary>
    private static (IWorldAccessor World, ICoreAPI Api, InventoryGeneric Inventory) CreateInventory()
    {
        var world = Mock.Of<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(value => value.World).Returns(world);
        var registry = new Mock<IClassRegistryAPI>();
        registry.Setup(value => value.CreateInvNetworkUtil(It.IsAny<InventoryBase>(), api.Object))
            .Returns(Mock.Of<IInventoryNetworkUtil>());
        api.Setup(value => value.ClassRegistry).Returns(registry.Object);
        var inventory = new InventoryGeneric(1, "foodindicator", "test", null!)
        {
            Api = api.Object,
            InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>()
        };
        return (world, api.Object, inventory);
    }
    #endregion
}
