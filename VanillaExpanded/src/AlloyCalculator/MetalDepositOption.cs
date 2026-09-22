using System.Collections.Immutable;

using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

internal sealed record MetalDepositOption(
    AssetLocation OutputCode,
    ImmutableArray<MetalDepositIngredient> Ingredients);
