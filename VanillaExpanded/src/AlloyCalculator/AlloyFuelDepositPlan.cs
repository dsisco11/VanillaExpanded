using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Describes the desired fuel-slot state and the current state against which it was planned.</summary>
internal sealed record AlloyFuelDepositPlan(
    ItemStack DesiredStack,
    int DesiredAmount,
    ItemStack? ExpectedCurrentStack,
    int ExpectedCurrentAmount);