using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Samples the next selected arrow and counts its type using the same native eligible traversal.</summary>
internal static class BowAmmunitionSampler
{
    #region Public API
    /// <summary>Copies the selected icon and sums positive matching stacks without mutating any inventory.</summary>
    public static BowAmmunitionSample Sample(ItemBow bow, EntityPlayer player)
    {
        ItemStack? selected = BowAmmunitionSelector.Select(bow, player)?.Itemstack;
        if (selected == null || selected.Collectible == null || selected.StackSize <= 0) return new(null, 0);
        ItemStack icon = selected.Clone();
        long quantity = 0;
        // WalkInventory preserves opened external inventories and the engine's creative-inventory exclusion.
        player.WalkInventory(slot =>
        {
            ItemStack? stack = slot.Itemstack;
            if (slot is not ItemSlotCreative && stack != null && stack.StackSize > 0
                && ReferenceEquals(stack.Collectible, selected.Collectible)) quantity += stack.StackSize;
            return true;
        });
        return new(icon, quantity);
    }
    #endregion
}
