using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class CraftingClientPolicyNativeTests
{
    [Fact]
    public void PolicyUsesNativeTraitsForRecipeAccess()
    {
        var f = new AllocationFixture();
        var characters = new CharacterSystem();
        var loader = new Mock<IModLoader>();
        loader.Setup(mods => mods.GetModSystem<CharacterSystem>(true)).Returns(characters);
        f.Api.Setup(api => api.ModLoader).Returns(loader.Object);
        var first = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        var policy = new CraftingClientPolicy(f.Api.Object, f.Player);
        Assert.Null(first.RequiresTrait);
        Assert.True(policy.CanAccess(first));
        first.RequiresTrait = "unavailable";
        // Native CharacterSystem intentionally permits an absent character class.
        Assert.True(policy.CanAccess(first));
        f.Player.Entity.WatchedAttributes.SetString("characterClass", "unknown");
        Assert.False(policy.CanAccess(first));
    }
}
