using System;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.LiquidContainerIndicators;

/// <summary>Shows the fraction of a liquid container's capacity currently occupied.</summary>
internal sealed class LiquidContainerIndicatorProvider : IItemSlotIndicatorProvider
{
    private readonly LiquidContainerParticleColor particleColors = new();

    #region Public API
    /// <summary>Reads current litres on each query so transfers are reflected immediately.</summary>
    public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
    {
        indicator = default;
        if (!VanillaExpandedModSystem.Config.EnableLiquidContainerIndicators) return false;
        if (slot.Itemstack is not { Collectible: BlockLiquidContainerBase container } stack)
        {
            return false;
        }

        float capacity = container.CapacityLitres;
        if (!float.IsFinite(capacity) || capacity <= 0) return false;

        // Let the owning container convert liquid portions to litres; stack count is not volume.
        float litres = container.GetCurrentLitres(stack);
        if (!float.IsFinite(litres)) return false;
        // Food-capable vessels such as bowls have no liquid identity while empty. Use the game's
        // serving-container metadata, rather than matching bowl names or hiding empty dedicated vessels.
        if (container.Attributes?.IsTrue("mealContainer") == true && litres <= 0) return false;
        float fill = Math.Clamp(litres / capacity, 0, 1);
        // Keep actual volume in the sample; rendering retains a visible liquid surface even at empty.
        var color = IndicatorColorPallette.WithOpacity(
            ColorUtilEx.MultiLerp(IndicatorColorPallette.WaterColors.AsSpan(), fill), 0.5f);
        ItemStack? liquid = container.GetContent(stack);
        if (liquid is not null && slot.Inventory?.Api is ICoreClientAPI client
            && !(liquid.Collectible.Code?.Domain == "game"
                && liquid.Collectible.Code.Path is "waterportion" or "saltwaterportion" or "boilingwaterportion"))
            color = particleColors.Resolve(client, stack, liquid, color);
        indicator = new ItemSlotIndicator(fill, color, LiquidSloshIndicatorEffect.DrawRange);
        return true;
    }
    #endregion
}
