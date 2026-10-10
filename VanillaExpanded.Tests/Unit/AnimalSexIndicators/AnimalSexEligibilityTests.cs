using Vintagestory.API.Common;
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
