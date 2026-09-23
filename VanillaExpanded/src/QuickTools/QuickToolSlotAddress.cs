using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Identifies a preferred return destination without retaining a source slot or item identity.</summary>
internal readonly record struct QuickToolSlotAddress(IInventory Inventory, int Index);
