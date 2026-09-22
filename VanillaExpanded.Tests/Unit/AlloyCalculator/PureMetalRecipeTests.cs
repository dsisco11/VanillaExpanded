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

    [Fact]
    public void FindOptionForContents_MatchingAlloy_ReturnsAlloyOption()
    {
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MockItem tinIngot = CreateItem(2, "ingot-tin");
        MockItem bronzeIngot = CreateItem(3, "ingot-bronze");
        MockItem copperBits = CreateSmeltable(4, "metalbit-copper", copperIngot);
        MockItem tinBits = CreateSmeltable(5, "metalbit-tin", tinIngot);
        AlloyRecipe recipe = CreateAlloyRecipe(copperIngot, tinIngot, bronzeIngot);
        MetalDepositOption option = AlloyCalculatorLogic.FromAlloyRecipe(recipe);

        MetalDepositOption? result = AlloyCalculatorLogic.FindOptionForContents(
            [new ItemStack(copperBits, 9), new ItemStack(tinBits, 1)],
            [option],
            [recipe]);

        Assert.Same(option, result);
    }

    [Fact]
    public void FindOptionForContents_InvalidAlloyRatio_ReturnsNull()
    {
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MockItem tinIngot = CreateItem(2, "ingot-tin");
        MockItem bronzeIngot = CreateItem(3, "ingot-bronze");
        MockItem copperBits = CreateSmeltable(4, "metalbit-copper", copperIngot);
        MockItem tinBits = CreateSmeltable(5, "metalbit-tin", tinIngot);
        AlloyRecipe recipe = CreateAlloyRecipe(copperIngot, tinIngot, bronzeIngot);
        MetalDepositOption option = AlloyCalculatorLogic.FromAlloyRecipe(recipe);

        MetalDepositOption? result = AlloyCalculatorLogic.FindOptionForContents(
            [new ItemStack(copperBits, 5), new ItemStack(tinBits, 5)],
            [option],
            [recipe]);

        Assert.Null(result);
    }

    [Fact]
    public void FindOptionForContents_PureMetalVariants_ReturnsPureMetalOption()
    {
        MockItem copperIngot = CreateItem(1, "ingot-copper");
        MockItem copperBits = CreateSmeltable(2, "metalbit-copper", copperIngot);
        MockItem copperNuggets = CreateSmeltable(3, "nugget-copper", copperIngot);
        MetalDepositOption option = AlloyCalculatorLogic.CreatePureMetalOption(new ItemStack(copperIngot));

        MetalDepositOption? result = AlloyCalculatorLogic.FindOptionForContents(
            [new ItemStack(copperBits, 2), new ItemStack(copperNuggets, 3)],
            [option],
            []);

        Assert.Same(option, result);
    }

    private static AlloyRecipe CreateAlloyRecipe(MockItem copper, MockItem tin, MockItem output)
    {
        return new AlloyRecipe
        {
            Enabled = true,
            Output = new JsonItemStack
            {
                Code = output.Code,
                ResolvedItemstack = new ItemStack(output)
            },
            Ingredients =
            [
                new MetalAlloyIngredient
                {
                    Code = copper.Code,
                    ResolvedItemstack = new ItemStack(copper),
                    MinRatio = 0.88f,
                    MaxRatio = 0.92f
                },
                new MetalAlloyIngredient
                {
                    Code = tin.Code,
                    ResolvedItemstack = new ItemStack(tin),
                    MinRatio = 0.08f,
                    MaxRatio = 0.12f
                }
            ]
        };
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
