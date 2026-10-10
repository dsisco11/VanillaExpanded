using Vintagestory.API.Common;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Stores an owned presentation copy and total positive quantity of its collectible identity.</summary>
internal sealed record BowAmmunitionSample(ItemStack? Icon, long Quantity);
