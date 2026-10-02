using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Accepts a snapshot of initial collectible codes while leaving attribute compatibility to engine slots.</summary>
internal class MatchingContentsPolicy : AutoStashPolicy
{
    private readonly System.Func<ItemStack, bool> accepts;
    public override PreferredSlotCompatibility? PreferredSlot { get; }

    #region Public API
    /// <summary>Captures initial item types once so later deposited contents cannot expand eligibility.</summary>
    public MatchingContentsPolicy(IEnumerable<ItemStack?> contents)
    {
        HashSet<AssetLocation> types = [.. contents.Where(stack => stack?.Collectible is not null)
            .Select(stack => stack!.Collectible.Code)];
        accepts = stack => types.Contains(stack.Collectible.Code);
    }

    /// <summary>Preserves predicate-based fixtures and the characterized preferred-slot compatibility contract.</summary>
    public MatchingContentsPolicy(System.Func<ItemStack, bool> accepts, System.Func<ItemStack, int?>? preference = null)
    {
        this.accepts = accepts;
        PreferredSlot = preference is null ? null : new PreferredSlotCompatibility(preference);
    }

    /// <summary>Applies code eligibility without duplicating the engine's destination compatibility checks.</summary>
    public override bool IsEligible(ItemStack stack, AutoStashSourcePass pass) => accepts(stack);
    #endregion
}
