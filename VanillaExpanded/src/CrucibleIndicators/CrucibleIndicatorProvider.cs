using System;
using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.CrucibleIndicators;

/// <summary>Samples metal amount and selects either solid or molten crucible presentation.</summary>
internal sealed class CrucibleIndicatorProvider(bool molten) : IItemSlotIndicatorProvider
{
    private readonly CrucibleMetalColors colors = new();
    private readonly CrucibleParticlePalette particles = new();
    #region Public API
    /// <summary>Reads poured metal or the active firepit's ingredients without modifying either inventory.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        var config = VanillaExpandedModSystem.Config;
        if (!config.EnableCrucibleIndicators || slot.Itemstack is not { } stack
            || slot.Inventory?.Api is not { } api) return false;
        ItemStack? metal;
        double units;
        float temperature;
        bool isMolten = false;
        ItemStack[] particleContents;
        if (stack.Collectible is BlockSmeltedContainer container)
        {
            var contents = container.GetContents(api.World, stack);
            metal = contents.Key;
            units = contents.Value;
            if (metal is null || units <= 0) return false;
            particleContents = [metal];
            temperature = container.GetTemperature(api.World, stack);
            // Use the game's pouring threshold, not the ceramic container's own melting point.
            isMolten = !container.HasSolidifed(stack, metal, api.World);
        }
        else if (stack.Collectible is BlockSmeltingContainer input
            && slot.Inventory is InventorySmelting inventory && ReferenceEquals(inventory[1], slot))
        {
            var ingredients = input.GetIngredients(api.World, inventory);
            particleContents = ingredients;
            var alloy = input.GetMatchingAlloy(api.World, ingredients);
            if (alloy is not null)
            {
                metal = alloy.Output.ResolvedItemstack;
                units = Math.Round(alloy.GetTotalOutputQuantity(ingredients) * 100);
            }
            else
            {
                var match = BlockSmeltingContainer.GetSingleSmeltableStack(ingredients);
                metal = match?.output;
                units = match is null ? 0 : Math.Round(match.stackSize * 100);
            }
            // Ingredients remain solid until the game actually replaces the crucible with its smelted form.
            temperature = input.GetIngredientsTemperature(api.World, ingredients);
        }
        else return false;
        float capacity = config.CrucibleIndicatorCapacityUnits;
        if (metal is null || !double.IsFinite(units) || units <= 0 || !float.IsFinite(temperature)
            || !float.IsFinite(capacity) || capacity <= 0 || isMolten != molten) return false;
        Vector3 baseColor = colors.Resolve(api as ICoreClientAPI, metal);
        float heat = Math.Clamp((temperature - 500) / 700, 0, 1);
        Vector3 tint = Vector3.Lerp(baseColor, new(1, 0.48f, 0.08f), heat);
        indicator = new((float)Math.Clamp(units / capacity, 0, 1), new Vector4(tint, 0.55f),
            CrucibleIndicatorEffect.DrawRange)
        {
            ParticlePalette = molten ? null : particles.Resolve(api as ICoreClientAPI, stack, particleContents)
        };
        return true;
    }
    #endregion
}
