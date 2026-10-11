using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Describes an ingredient quantity and its optional concrete stack form for one cooking slot.</summary>
internal sealed record AlloyDepositSlotTarget(
    int SlotIndex,
    MetalDepositIngredient Ingredient,
    int Amount,
    ItemStack? DesiredStack = null);
