using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VanillaExpanded.AnimalSexIndicators;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.Tests.Unit.AnimalSexIndicators;

/// <summary>Checks synchronized eligibility boundaries without a world or graphics context.</summary>
public sealed class AnimalSexEligibilityTests
{
    #region Public API
    /// <summary>Accepts either recognized sex at every bred generation, including the threshold.</summary>
    [Theory]
    [InlineData(1, "male")]
    [InlineData(1, "female")]
    [InlineData(10, "male")]
    public void BredAnimalsHaveRecognizedSex(int generation, string gender)
    {
        var animal = new TestAnimal(gender);
        animal.WatchedAttributes.SetInt("generation", generation);
        Assert.Equal(gender, AnimalSexEligibility.GetSex(animal));
    }

    /// <summary>Suppresses wild, invalid, dead, and unrecognized animals.</summary>
    [Theory]
    [InlineData(0, "male", true)]
    [InlineData(-1, "female", true)]
    [InlineData(1, "unknown", true)]
    [InlineData(1, "male", false)]
    [InlineData(1, null, true)]
    public void IneligibleAnimalsHaveNoIcon(int generation, string? gender, bool alive)
    {
        var animal = new TestAnimal(gender) { Alive = alive };
        animal.WatchedAttributes.SetInt("generation", generation);
        Assert.Null(AnimalSexEligibility.GetSex(animal));
    }

    /// <summary>Does not use breeding-looking attributes on players or noncreatures.</summary>
    [Fact]
    public void NonAnimalsHaveNoIcon()
    {
        var entity = new TestEntity();
        entity.WatchedAttributes.SetInt("generation", 1);
        Assert.Null(AnimalSexEligibility.GetSex(entity));
        var player = new EntityPlayer();
        player.WatchedAttributes.SetInt("generation", 1);
        Assert.Null(AnimalSexEligibility.GetSex(player));
    }
    /// <summary>Uses a heart only for eligible pregnant females and tolerates absent breeding data.</summary>
    [Theory]
    [InlineData(1, "female", true, true, "female-pregnant")]
    [InlineData(1, "female", true, false, "female")]
    [InlineData(1, "female", true, null, "female")]
    [InlineData(1, "male", true, true, "male")]
    [InlineData(0, "female", true, true, null)]
    [InlineData(1, "female", false, true, null)]
    public void PregnancyVariantRespectsEligibility(int generation, string gender, bool alive, bool? pregnant, string? expected)
    {
        var animal = new TestAnimal(gender) { Alive = alive };
        animal.WatchedAttributes.SetInt("generation", generation);
        if (pregnant.HasValue)
        {
            var breeding = new TreeAttribute();
            breeding.SetBool("isPregnant", pregnant.Value);
            animal.WatchedAttributes["multiply"] = breeding;
        }
        Assert.Equal(expected, AnimalSexEligibility.GetIconVariant(animal));
    }

    /// <summary>Reads current pregnancy state after conception, birth, and watched-tree replacement.</summary>
    [Fact]
    public void PregnancyChangesUpdateVariant()
    {
        var animal = new TestAnimal("female");
        animal.WatchedAttributes.SetInt("generation", 1);
        var breeding = new TreeAttribute();
        animal.WatchedAttributes["multiply"] = breeding;
        Assert.Equal("female", AnimalSexEligibility.GetIconVariant(animal));
        breeding.SetBool("isPregnant", true);
        Assert.Equal("female-pregnant", AnimalSexEligibility.GetIconVariant(animal));
        breeding.SetBool("isPregnant", false);
        Assert.Equal("female", AnimalSexEligibility.GetIconVariant(animal));
        var replacement = new TreeAttribute();
        replacement.SetBool("isPregnant", true);
        animal.WatchedAttributes["multiply"] = replacement;
        Assert.Equal("female-pregnant", AnimalSexEligibility.GetIconVariant(animal));
    }
    #endregion

    /// <summary>Represents a noncreature without requiring world initialization.</summary>
    private sealed class TestEntity : Entity { }

    /// <summary>Supplies resolved entity variants while retaining real synchronized attributes.</summary>
    private sealed class TestAnimal : EntityAgent
    {
        /// <summary>Creates an animal with an optional known gender variant.</summary>
        public TestAnimal(string? gender)
        {
            Properties = new EntityProperties();
            if (gender is not null) Properties.Variant["gender"] = gender;
        }
    }
}
