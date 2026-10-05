using System;
using System.Runtime.CompilerServices;
using VanillaExpanded.ItemSlotIndicators;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CrucibleIndicators;

/// <summary>Retains individual ore or metal texture colors for solid crucible pieces.</summary>
internal sealed class CrucibleParticlePalette
{
    private readonly ConditionalWeakTable<ItemStack, Sample> samples = new();

    #region Public API
    /// <summary>Samples actual ingredients, or persisted output metal, only when their collectible identities change.</summary>
    internal ItemSlotIndicatorParticlePalette Resolve(ICoreClientAPI? client, ItemStack crucible, ItemStack[] contents)
    {
        if (samples.TryGetValue(crucible, out var previous) && Matches(previous, client, contents)) return previous.Palette;
        var identities = new CollectibleObject?[contents.Length];
        int count = 0;
        foreach (var content in contents)
            if (content is not null) identities[count++] = content.Collectible;
        Array.Resize(ref identities, count);
        Span<int> colors = stackalloc int[ItemSlotIndicatorParticlePalette.ColorCount];
        // Preserve distinct ingredient pixels instead of collapsing them into the output ingot's average.
        int cursor = 0;
        for (int index = 0; index < colors.Length; index++)
        {
            ItemStack? ingredient = null;
            if (count > 0)
            {
                do { ingredient = contents[cursor++ % contents.Length]; } while (ingredient is null);
            }
            int packed = client is not null && ingredient is not null
                ? ingredient.Collectible.GetRandomColor(client, ingredient) : 0;
            colors[index] = packed == 0 ? unchecked((int)0xFF8C9199) : packed;
        }
        var palette = new ItemSlotIndicatorParticlePalette(colors);
        samples.Remove(crucible);
        samples.Add(crucible, new Sample(client, identities, palette));
        return palette;
    }
    #endregion

    #region Private
    /// <summary>Ignores empty cooking slots while detecting changed ingredient identities and clients.</summary>
    private static bool Matches(Sample sample, ICoreClientAPI? client, ItemStack[] contents)
    {
        if (!ReferenceEquals(sample.Client, client)) return false;
        int index = 0;
        foreach (var content in contents)
        {
            if (content is null) continue;
            if (index >= sample.Ingredients.Length || !ReferenceEquals(sample.Ingredients[index++], content.Collectible)) return false;
        }
        return index == sample.Ingredients.Length;
    }

    /// <summary>Stores an immutable palette with the context that produced its texture samples.</summary>
    private sealed record Sample(ICoreClientAPI? Client, CollectibleObject?[] Ingredients, ItemSlotIndicatorParticlePalette Palette);
    #endregion
}
