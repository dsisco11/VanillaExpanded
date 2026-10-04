using Moq;
using VanillaExpanded.FoodContainerIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.FoodContainerIndicators;

/// <summary>Checks exclusive container routing and preservation of contained freshness.</summary>
[Trait("Category", "Unit")]
public sealed class FoodContainerIndicatorProviderTests
{
    #region Public API
    /// <summary>Only the matching provider accepts a direct perish state, including container states.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectPerishState_IsOwnedByExactlyOneProvider(bool foodContainer)
    {
        var (world, api, inventory) = CreateInventory();
        var slot = inventory[0];
        var item = new Mock<MockItem>(1, (byte)0, api);
        item.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>())
            .Returns(foodContainer ? Mock.Of<IBlockMealContainer>() : null!);
        item.Setup(value => value.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish))
            .Returns(new TransitionState { FreshHours = 100, FreshHoursLeft = 50 });
        slot.Itemstack = new ItemStack(item.Object);

        Assert.Equal(!foodContainer, new FreshnessIndicatorProvider().TryGetIndicator(slot, out var ordinary));
        Assert.Equal(foodContainer, new FoodContainerIndicatorProvider().TryGetIndicator(slot, out var contained));
        var actual = foodContainer ? contained : ordinary;
        Assert.Equal(0.5f, actual.Fill);
        Assert.Equal(FreshnessIndicatorProvider.FreshnessColor(0.5f), actual.Color);
        item.Verify(value => value.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish), Times.Once);
    }

    /// <summary>Empty and nonperishable meals stay hidden; perishable contents inherit inventory spoilage rates.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Contents_RequirePerishStateAndUseInventoryRates(bool hasContents, bool perishable)
    {
        var (world, api, inventory) = CreateInventory();
        var slot = inventory[0];
        var container = new Mock<MockItem>(1, (byte)0, api);
        var meal = new Mock<IBlockMealContainer>();
        container.Setup(value => value.GetCollectibleInterface<IBlockMealContainer>()).Returns(meal.Object);
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

        Assert.False(new FreshnessIndicatorProvider().TryGetIndicator(slot, out _));
        Assert.Equal(hasContents && perishable, new FoodContainerIndicatorProvider().TryGetIndicator(slot, out var indicator));
        if (hasContents && perishable) Assert.Equal(0.25f, indicator.Fill);
    }
    #endregion

    #region Private
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
