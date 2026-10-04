using Moq;
using VanillaExpanded.FoodContainerIndicators;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.FoodContainerIndicators;

/// <summary>Checks food color routing, cached stability, and engine color decoding.</summary>
[Trait("Category", "Unit")]
public sealed class FoodContainerParticlePaletteTests
{
    #region Public API
    /// <summary>Ingredients supply colors instead of vessel texture, and replacement refreshes the palette.</summary>
    [Fact]
    public void Ingredients_SupplyStablePaletteUntilTheirIdentityChanges()
    {
        var world = Mock.Of<IClientWorldAccessor>();
        var api = new Mock<ICoreClientAPI>();
        api.Setup(value => value.World).Returns(world);
        var vessel = new Mock<MockItem>(1, (byte)0, api.Object);
        var stack = new ItemStack(vessel.Object);
        var food = new Mock<MockItem>(2, (byte)0, api.Object);
        var ingredient = new ItemStack(food.Object);
        food.Setup(value => value.GetRandomColor(api.Object, ingredient)).Returns(unchecked((int)0xFF123456));
        var meal = new Mock<IBlockMealContainer>();
        meal.Setup(value => value.GetNonEmptyContents(world, stack)).Returns(() => [ingredient]);
        var resolver = new FoodContainerParticlePalette();
        var first = resolver.Resolve(api.Object, stack, meal.Object);
        Assert.Same(first, resolver.Resolve(api.Object, stack, meal.Object));
        food.Verify(value => value.GetRandomColor(api.Object, ingredient), Times.Exactly(16));
        vessel.Verify(value => value.GetRandomColor(It.IsAny<ICoreClientAPI>(), It.IsAny<ItemStack>()), Times.Never);
        var shader = new Mock<IShaderProgram>();
        float[]? submitted = null;
        shader.Setup(value => value.Uniforms4("foodPalette", 16, It.IsAny<float[]>()))
            .Callback((string name, int count, float[] colors) => submitted = colors.ToArray());
        first.Submit(shader.Object);
        Assert.NotNull(submitted);
        Assert.Equal(0x12 / 255f, submitted[0]);
        Assert.Equal(0x34 / 255f, submitted[1]);
        Assert.Equal(0x56 / 255f, submitted[2]);
        Assert.Equal(1, submitted[3]);
        var nextFood = new Mock<MockItem>(3, (byte)0, api.Object);
        ingredient = new ItemStack(nextFood.Object);
        nextFood.Setup(value => value.GetRandomColor(api.Object, ingredient)).Returns(unchecked((int)0xFFABCDEF));
        Assert.NotSame(first, resolver.Resolve(api.Object, stack, meal.Object));
        nextFood.Verify(value => value.GetRandomColor(api.Object, ingredient), Times.Exactly(16));
    }
    #endregion
}
