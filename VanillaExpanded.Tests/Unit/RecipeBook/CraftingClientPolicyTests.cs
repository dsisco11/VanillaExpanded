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

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class CraftingClientPolicyTests
{
    [Fact]
    public void ProductionPolicyDoesNotInspectOrRejectCustomEventSubscriptions()
    {
        var f = new AllocationFixture();
        var loader = new Mock<IModLoader>();
        loader.Setup(mods => mods.GetModSystem<CharacterSystem>(true)).Returns(new CharacterSystem());
        f.Api.Setup(api => api.ModLoader).Returns(loader.Object);
        var events = (ClientEventAPI)RuntimeHelpers.GetUninitializedObject(typeof(ClientEventAPI));
        events.MatchesRecipe += (_, _, _) => true;
        events.MatchesGridRecipe += (_, _, _, _) => false;
        f.Api.Setup(api => api.Event).Returns(events);
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        f.Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe> { recipe });
        var policy = new CraftingClientPolicy(f.Api.Object, f.Player);
        Assert.True(policy.CanAccess(recipe));
        f.Api.VerifyGet(api => api.Event, Times.Never);
    }
}
