namespace VanillaExpanded.AlloyCalculator;

internal sealed record AlloyDepositSlotTarget(
    int SlotIndex,
    MetalDepositIngredient Ingredient,
    int Amount);