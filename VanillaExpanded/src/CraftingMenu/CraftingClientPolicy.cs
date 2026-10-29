using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingClientPolicy
{
    private readonly CharacterSystem characters;
    private readonly IPlayer player;

    internal CraftingClientPolicy(ICoreAPI api, IPlayer player)
    {
        this.player = player;
        characters = api.ModLoader.GetModSystem<CharacterSystem>();
    }

    internal bool CanAccess(GridRecipe recipe) =>
        recipe.RequiresTrait == null || characters.HasTrait(player, recipe.RequiresTrait);
}
