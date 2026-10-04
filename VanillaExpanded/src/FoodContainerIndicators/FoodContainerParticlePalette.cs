using System;
using System.Runtime.CompilerServices;
using VanillaExpanded.ItemSlotIndicators;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.FoodContainerIndicators;

/// <summary>Caches engine particle colors per food stack and ingredient identity to avoid resampling flicker.</summary>
internal sealed class FoodContainerParticlePalette
{
    private readonly ConditionalWeakTable<ItemStack, Sample> samples = new();

    #region Public API
    /// <summary>Samples meal colors only when the client or ingredient collectibles change.</summary>
    internal ItemSlotIndicatorParticlePalette Resolve(ICoreClientAPI api, ItemStack stack, IBlockMealContainer container)
    {
        ItemStack[] contents = container.GetNonEmptyContents(api.World, stack) ?? [];
        if (samples.TryGetValue(stack, out var cached) && Matches(cached, api, contents)) return cached.Palette;
        var identities = new CollectibleObject[contents.Length];
        for (int i = 0; i < contents.Length; i++) identities[i] = contents[i].Collectible;
        Span<int> colors = stackalloc int[ItemSlotIndicatorParticlePalette.ColorCount];
        // Sample ingredient APIs directly: storage vessels such as crocks can otherwise return ceramic colors.
        // Cycling ingredients keeps small sampled palettes representative while each API chooses its particle pixel.
        for (int i = 0; i < colors.Length; i++)
        {
            ItemStack food = contents.Length > 0 ? contents[i % contents.Length] : stack;
            colors[i] = food.Collectible.GetRandomColor(api, food);
        }
        var palette = new ItemSlotIndicatorParticlePalette(colors);
        samples.Remove(stack);
        samples.Add(stack, new Sample(api, identities, palette));
        return palette;
    }
    #endregion

    #region Private
    /// <summary>Compares ingredient identities while ignoring transition timers that do not alter food particle colors.</summary>
    private static bool Matches(Sample sample, ICoreClientAPI api, ItemStack[] contents)
    {
        if (!ReferenceEquals(sample.Api, api) || sample.Ingredients.Length != contents.Length) return false;
        for (int i = 0; i < contents.Length; i++)
            if (!ReferenceEquals(sample.Ingredients[i], contents[i].Collectible)) return false;
        return true;
    }

    /// <summary>Retains client and ingredient context alongside the immutable sampled palette.</summary>
    private sealed record Sample(ICoreClientAPI Api, CollectibleObject[] Ingredients, ItemSlotIndicatorParticlePalette Palette);
    #endregion
}
