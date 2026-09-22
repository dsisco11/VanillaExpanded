using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class MetalDepositOptionTests
{
    [Fact]
    public void CreatePureMetalOptions_MetalBit_AddsSingleIngredientOption()
    {
        // Arrange
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MockItem copperBits = CreateSmeltable(2, "metalbit-copper", copperIngot);

        // Act
        MetalDepositOption option = Assert.Single(AlloyCalculatorLogic.CreatePureMetalOptions(
            [new ItemStack(copperBits)],
            [],
            maxFuelTemperature: 1300));

        // Assert
        Assert.Equal(copperIngot.Code, option.OutputCode);
        MetalDepositIngredient ingredient = Assert.Single(option.Ingredients);
        Assert.Equal(copperIngot.Code, ingredient.Code);
        Assert.Equal(1, ingredient.MinRatio);
        Assert.Equal(1, ingredient.MaxRatio);
    }

    [Fact]
    public void CreatePureMetalOptions_RegisteredOutput_DoesNotAddDuplicate()
    {
        // Arrange
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MockItem copperBits = CreateSmeltable(2, "metalbit-copper", copperIngot);
        MetalDepositOption registered = AlloyCalculatorLogic.CreatePureMetalOption(new ItemStack(copperIngot));

        // Act
        List<MetalDepositOption> options = AlloyCalculatorLogic.CreatePureMetalOptions(
            [new ItemStack(copperBits)],
            [registered],
            maxFuelTemperature: 1300);

        // Assert
        Assert.Empty(options);
    }

    [Fact]
    public void CreatePureMetalOptions_MetalBitWithoutCombustibleProperties_SkipsEntry()
    {
        // Arrange
        MockItem incompleteMetalBit = CreateItem(1, "metalbit-incomplete");

        // Act
        List<MetalDepositOption> options = AlloyCalculatorLogic.CreatePureMetalOptions(
            [new ItemStack(incompleteMetalBit)],
            [],
            maxFuelTemperature: 1300);

        // Assert
        Assert.Empty(options);
    }

    [Fact]
    public void CreatePureMetalOptions_UnresolvedSmeltedStack_SkipsEntry()
    {
        // Arrange
        MockItem incompleteMetalBit = CreateItem(1, "metalbit-incomplete");
        incompleteMetalBit.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 1000,
            SmeltedStack = new JsonItemStack
            {
                Code = new AssetLocation("game", "ingot-incomplete")
            }
        };

        // Act
        List<MetalDepositOption> options = AlloyCalculatorLogic.CreatePureMetalOptions(
            [new ItemStack(incompleteMetalBit)],
            [],
            maxFuelTemperature: 1300);

        // Assert
        Assert.Empty(options);
    }

    [Fact]
    public void CreatePureMetalOptions_NullHandbookEntry_SkipsEntry()
    {
        List<MetalDepositOption> options = AlloyCalculatorLogic.CreatePureMetalOptions(
            [null],
            [],
            maxFuelTemperature: 1300);

        Assert.Empty(options);
    }

    [Fact]
    public void ShouldShowRatioControls_SingleMetal_ReturnsFalse()
    {
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MetalDepositOption option = AlloyCalculatorLogic.CreatePureMetalOption(new ItemStack(copperIngot));

        Assert.False(AlloyCalculatorLogic.ShouldShowRatioControls(option));
    }

    [Fact]
    public void ShouldShowRatioControls_MultipleMetals_ReturnsTrue()
    {
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MockItem tinIngot = CreateItem(2, "ingot-tin");
        var option = new MetalDepositOption(
            new AssetLocation("game", "ingot-bronze"),
            [
                new MetalDepositIngredient(copperIngot.Code, new ItemStack(copperIngot), 0.88f, 0.92f),
                new MetalDepositIngredient(tinIngot.Code, new ItemStack(tinIngot), 0.08f, 0.12f)
            ]);

        Assert.True(AlloyCalculatorLogic.ShouldShowRatioControls(option));
    }

    private static MockItem CreateItem(int id, string path)
    {
        var item = MockItem.CreateNonLightSource(id);
        item.Code = new AssetLocation("game", path);
        return item;
    }

    private static MockItem CreateSmeltable(int id, string path, MockItem ingot)
    {
        MockItem item = CreateItem(id, path);
        item.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 1000,
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack
            {
                Code = ingot.Code,
                ResolvedItemstack = new ItemStack(ingot)
            }
        };
        return item;
    }
}
