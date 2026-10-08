using Vintagestory.API.Common;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Identifies a carried source stack and its detached presentation snapshot.</summary>
internal sealed record ChiselMaterialCandidate(InventoryBase Inventory, int SlotIndex, ItemStack SourceStack, ItemStack Stack);
