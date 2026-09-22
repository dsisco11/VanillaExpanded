using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

internal sealed record MetalDepositIngredient(
    AssetLocation Code,
    ItemStack ResolvedStack,
    float MinRatio,
    float MaxRatio);
