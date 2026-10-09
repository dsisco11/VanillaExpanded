using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Preserves exclusive valid-index preflight and optional execution preference with fallback.</summary>
internal sealed class PreferredSlotCompatibility
{
    private readonly System.Func<ItemStack, int?> select;

    #region Public API
    /// <summary>Captures the existing per-stack preference without converting it into mandatory routing.</summary>
    public PreferredSlotCompatibility(System.Func<ItemStack, int?> select) => this.select = select;

    /// <summary>Resolves a current valid preference; invalid indices are ordinary-selection requests.</summary>
    public int? GetIndex(ItemStack stack, int count)
    {
        int? index = select(stack);
        return index >= 0 && index < count ? index : null;
    }
    #endregion
}
