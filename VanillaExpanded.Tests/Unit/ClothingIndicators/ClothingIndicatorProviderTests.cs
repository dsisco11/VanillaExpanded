using Moq;

using VanillaExpanded.ClothingIndicators;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.ClothingIndicators;

/// <summary>Checks clothing condition indicators against the game's wearable behavior contract.</summary>
[Trait("Category", "Unit")]
public sealed class ClothingIndicatorProviderTests
{
    #region Public API
    #region Eligibility
    /// <summary>Empty slots and ordinary collectibles remain unadorned even with a condition attribute.</summary>
    [Fact]
    public void NonWearable_ReturnsFalse()
    {
        var provider = new ClothingIndicatorProvider();
        var slot = new ItemSlot(null);
        Assert.False(provider.TryGetIndicator(slot, out _));
        slot.Itemstack = new ItemStack(MockItem.CreateNonLightSource(1));
        slot.Itemstack.Attributes.SetFloat("condition", 0.5f);
        Assert.False(provider.TryGetIndicator(slot, out _));
    }

    /// <summary>Armor uses vanilla durability even when its wearable behavior reports warmth.</summary>
    [Theory]
    [InlineData(EnumCharacterDressType.ArmorBody)]
    [InlineData(EnumCharacterDressType.ArmorHead)]
    [InlineData(EnumCharacterDressType.ArmorLegs)]
    public void Armor_ReturnsFalse(EnumCharacterDressType dressType)
    {
        var slot = CreateClothing(0.5f);
        var wearable = new Mock<CollectibleBehaviorWearable>(slot.Itemstack!.Collectible) { CallBase = true };
        wearable.Setup(value => value.GetDressType(slot)).Returns(dressType);
        slot.Itemstack.Collectible.CollectibleBehaviors = [wearable.Object];
        Assert.False(new ClothingIndicatorProvider().TryGetIndicator(slot, out _));
    }

    /// <summary>Wearables without useful warmth do not receive a condition gauge.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveWarmth_ReturnsFalse(float warmth)
    {
        Assert.False(new ClothingIndicatorProvider().TryGetIndicator(CreateClothing(0.5f, warmth), out _));
    }

    /// <summary>Uninitialized condition stays absent so rendering cannot invent or persist its value.</summary>
    [Fact]
    public void MissingCondition_DoesNotInitializeOrMutateStack()
    {
        var slot = CreateClothing(null);
        var attributes = slot.Itemstack!.Attributes.ToJsonToken().ToString();
        Assert.False(new ClothingIndicatorProvider().TryGetIndicator(slot, out _));
        Assert.False(slot.Itemstack.Attributes.HasAttribute("condition"));
        Assert.Equal(attributes, slot.Itemstack.Attributes.ToJsonToken().ToString());
    }

    /// <summary>Derived wearable behaviors retain their overridden warmth contract.</summary>
    [Fact]
    public void DerivedWearable_UsesOverriddenWarmth()
    {
        var slot = CreateClothing(0.5f, 0);
        var wearable = new Mock<CollectibleBehaviorWearable>(slot.Itemstack!.Collectible) { CallBase = true };
        wearable.Setup(value => value.GetMaxWarmth(slot)).Returns(2);
        slot.Itemstack.Collectible.CollectibleBehaviors = [wearable.Object];
        Assert.True(new ClothingIndicatorProvider().TryGetIndicator(slot, out var indicator));
        Assert.Equal(0.5f, indicator.Fill);
    }
    #endregion

    #region Condition And Color
    /// <summary>Fill reports actual condition, while full warmth begins at half condition.</summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1)]
    [InlineData(2)]
    public void FullWarmth_IsGreenWithActualClampedFill(float condition)
    {
        Assert.True(new ClothingIndicatorProvider().TryGetIndicator(CreateClothing(condition), out var indicator));
        Assert.Equal(Math.Min(condition, 1), indicator.Fill);
        Assert.Equal(IndicatorColorPallette.WithOpacity(IndicatorColorPallette.Green, 0.5f), indicator.Color);
    }

    /// <summary>Below the warmth threshold, condition retains its fraction and progresses toward green.</summary>
    [Fact]
    public void LowCondition_UsesActualFillAndWarmthGradient()
    {
        var provider = new ClothingIndicatorProvider();
        Assert.True(provider.TryGetIndicator(CreateClothing(0.1f), out var low));
        Assert.True(provider.TryGetIndicator(CreateClothing(0.25f), out var middle));
        Assert.True(provider.TryGetIndicator(CreateClothing(0.49f), out var high));
        Assert.Equal(0.1f, low.Fill);
        Assert.Equal(0.25f, middle.Fill);
        Assert.Equal(0.49f, high.Fill);
        Assert.True(low.Color.X > low.Color.Y);
        Assert.True(high.Color.Y > high.Color.X);
        Assert.NotEqual(low.Color, middle.Color);
    }

    /// <summary>Zero and negative condition give a full-height shared-red warning.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RuinedClothing_ProducesFullRedWarning(float condition)
    {
        Assert.True(new ClothingIndicatorProvider().TryGetIndicator(CreateClothing(condition), out var indicator));
        Assert.Equal(1, indicator.Fill);
        Assert.Equal(IndicatorColorPallette.WithOpacity(IndicatorColorPallette.Red, 0.3f), indicator.Color);
    }

    /// <summary>Nonfinite condition does not become a misleading warning or level.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidCondition_ReturnsFalse(float condition)
    {
        Assert.False(new ClothingIndicatorProvider().TryGetIndicator(CreateClothing(condition), out _));
    }

    /// <summary>Repairing and wearing the same garment change its next indicator without a cache delay.</summary>
    [Fact]
    public void ConditionChanges_UpdateImmediatelyWithoutMutation()
    {
        var slot = CreateClothing(0.25f);
        var provider = new ClothingIndicatorProvider();
        Assert.True(provider.TryGetIndicator(slot, out var worn));
        Assert.Equal(0.25f, worn.Fill);

        // Change the authoritative attribute as repair and wear do, then check read-only sampling.
        slot.Itemstack!.Attributes.SetFloat("condition", 0.75f);
        Assert.True(provider.TryGetIndicator(slot, out var repaired));
        Assert.Equal(0.75f, repaired.Fill);
        Assert.Equal(0.75f, slot.Itemstack.Attributes.GetFloat("condition"));
        slot.Itemstack.Attributes.SetFloat("condition", 0);
        Assert.True(provider.TryGetIndicator(slot, out var ruined));
        Assert.Equal(1, ruined.Fill);
        Assert.Equal(0, slot.Itemstack.Attributes.GetFloat("condition"));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a real wearable behavior with optional initialized stack condition.</summary>
    private static ItemSlot CreateClothing(float? condition, float warmth = 2)
    {
        var item = MockItem.CreateNonLightSource(1);
        item.Attributes = JsonObject.FromJson("{\"warmth\":" + warmth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
        item.CollectibleBehaviors = [new CollectibleBehaviorWearable(item)];
        var stack = new ItemStack(item);
        if (condition.HasValue) stack.Attributes.SetFloat("condition", condition.Value);
        return new ItemSlot(null) { Itemstack = stack };
    }
    #endregion
}
