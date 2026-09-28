using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Resolves collectible-specific menu geometry without coupling the input system to tool types.</summary>
internal static class ToolModeMenuLayoutStrategyRegistry
{
    private static readonly IReadOnlyDictionary<Type, IToolModeMenuLayoutStrategy> Strategies =
        new Dictionary<Type, IToolModeMenuLayoutStrategy>
        {
            [typeof(ItemHammer)] = SmithingHammerToolModeMenuLayoutStrategy.Instance,
            [typeof(ItemChisel)] = ChiselToolModeMenuLayoutStrategy.Instance,
            [typeof(ItemRoller)] = BoatRollerToolModeMenuLayoutStrategy.Instance
        };

    /// <summary>Resolves the nearest registered collectible base type, or the generic strategy.</summary>
    internal static IToolModeMenuLayoutStrategy Resolve(CollectibleObject collectible)
    {
        // Walking base types lets derived or patched collectibles inherit the nearest registered menu profile.
        for (Type? type = collectible.GetType(); type is not null; type = type.BaseType)
            if (Strategies.TryGetValue(type, out IToolModeMenuLayoutStrategy? strategy)) return strategy;
        return GenericToolModeMenuLayoutStrategy.Instance;
    }
}