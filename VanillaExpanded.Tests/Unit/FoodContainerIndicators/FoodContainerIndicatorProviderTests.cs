using Moq;
using Newtonsoft.Json.Linq;
using VanillaExpanded.FoodContainerIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.FoodContainerIndicators;

/// <summary>Checks portion data, serving capacity, and independent freshness.</summary>
[Trait("Category", "Unit")]
public sealed class FoodContainerIndicatorProviderTests : System.IDisposable
{
    private readonly string previousFoodEffect = VanillaExpandedModSystem.Config.FoodLevelIndicatorStyle;

    #region Public API
    /// <summary>Enables the optional effect explicitly for provider capability checks.</summary>
    public FoodContainerIndicatorProviderTests()
    {
        VanillaExpandedModSystem.Config.FoodLevelIndicatorStyle = "slot-background";
    }

    /// <summary>Restores the caller's effect setting after each test.</summary>
    public void Dispose()
    {
        VanillaExpandedModSystem.Config.FoodLevelIndicatorStyle = previousFoodEffect;
    }

    /// <summary>Both food styles retain amount and freshness; only the background uses particle framing.</summary>
    [Fact]
    public void FoodStyles_PreserveAmountAndFreshness()
    {
        var config = VanillaExpandedModSystem.Config;
        string previous = config.FoodLevelIndicatorStyle;
        try
        {
            var (world, api, inventory) = CreateInventory();
            var item = new Mock<MockItem>(1, (byte)0, api);
            item.Object.Attributes = JsonObject.FromJson("{\"mealContainer\":true}");
            item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(CreateMeal(world));
            item.Setup(value => value.UpdateAndGetTransitionState(world, inventory[0], EnumTransitionType.Perish))
                .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
            inventory[0].Itemstack = new ItemStack(item.Object);
            config.FoodLevelIndicatorStyle = "progress-bar";
            Assert.True(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out var bar));
            Assert.Null(bar.DrawRange);
            Assert.True(new FreshnessIndicatorProvider().TryGetIndicator(inventory[0], out _));
            config.FoodLevelIndicatorStyle = "slot-background";
            Assert.True(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out _));
        }
        finally { config.FoodLevelIndicatorStyle = previous; }
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

    /// <summary>Pies normalize their portions by slice count without altering freshness.</summary>
    [Theory]
    [InlineData(1, 0.25f, 1)]
    [InlineData(1, 0.125f, 0.5f)]
    [InlineData(1, 0.0625f, 0.25f)]
    [InlineData(2, 0.5f, 1)]
    [InlineData(4, 1, 1)]
    public void Pie_NormalizesPortionsIndependentlyOfFreshness(int slices, float servings, float fill)
    {
        var (world, _, inventory) = CreateInventory();
        var pie = new Mock<BlockPie> { CallBase = true };
        pie.Setup(value => value.UpdateAndGetTransitionState(world, inventory[0], EnumTransitionType.Perish))
            .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
        var stack = new ItemStack(pie.Object);
        stack.Attributes.SetInt("pieSize", slices);
        stack.Attributes.SetFloat("quantityServings", servings);
        inventory[0].Itemstack = stack;
        Assert.NotNull(pie.Object.GetCollectibleInterface<IBlockMealContainer>());
        Assert.True(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out var portion));
        Assert.Equal(fill, portion.Fill);
        Assert.True(new FreshnessIndicatorProvider().TryGetIndicator(inventory[0], out var indicator));
        Assert.Equal(0.5f, indicator.Fill);
        Assert.Null(indicator.DrawRange);
        Assert.Null(indicator.ParticlePalette);
    }

    /// <summary>Portion data works without meal handling or vessel metadata.</summary>
    [Theory]
    [InlineData(0.5f, 1, true, 0.5f)]
    [InlineData(3, 4, true, 0.75f)]
    [InlineData(5, 4, true, 1)]
    [InlineData(0, 1, false, 0)]
    [InlineData(-1, 1, false, 0)]
    [InlineData(float.NaN, 1, false, 0)]
    [InlineData(float.PositiveInfinity, 1, false, 0)]
    [InlineData(1, 0, false, 0)]
    [InlineData(1, -1, false, 0)]
    public void PortionData_DoesNotRequireMealInterface(float servings, float capacity, bool visible, float fill)
    {
        var (_, api, inventory) = CreateInventory();
        var item = new MockItem(1, 0, api)
        {
            Attributes = new JsonObject(new JObject { ["servingCapacity"] = capacity })
        };
        var stack = new ItemStack(item);
        stack.Attributes.SetFloat("quantityServings", servings);
        inventory[0].Itemstack = stack;
        Assert.Equal(visible, new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out var indicator));
        if (visible) Assert.Equal(fill, indicator.Fill);
    }

    [Fact]
    public void OrdinaryItem_WithoutPortionDataHasNoIndicator()
    {
        var (_, api, inventory) = CreateInventory();
        inventory[0].Itemstack = new ItemStack(new MockItem(1, 0, api));
        Assert.False(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out _));
    }

    [Fact]
    public void PortionData_WithoutCapacityDefaultsToOneServing()
    {
        var (_, api, inventory) = CreateInventory();
        var stack = new ItemStack(new MockItem(1, 0, api));
        stack.Attributes.SetDouble("quantityServings", 0.75);
        inventory[0].Itemstack = stack;
        Assert.True(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out var indicator));
        Assert.Equal(0.75f, indicator.Fill);
    }

    [Fact]
    public void MealInterface_WithoutVesselMetadataStillReportsPortions()
    {
        var (world, api, inventory) = CreateInventory();
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(CreateMeal(world));
        inventory[0].Itemstack = new ItemStack(item.Object);
        Assert.True(new FoodContainerIndicatorProvider().TryGetIndicator(inventory[0], out var indicator));
        Assert.Equal(0.5f, indicator.Fill);
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
