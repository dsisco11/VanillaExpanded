using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.LiquidContainerIndicators;

/// <summary>Retains a stable liquid-surface tint sampled from the contained material's particle palette.</summary>
internal sealed class LiquidContainerParticleColor
{
    private readonly ConditionalWeakTable<ItemStack, Sample> samples = new();

    #region Public API
    /// <summary>Samples the liquid rather than the vessel, refreshing when client or material identity changes.</summary>
    internal Vector4 Resolve(ICoreClientAPI api, ItemStack vessel, ItemStack liquid, Vector4 fallback)
    {
        if (samples.TryGetValue(vessel, out var cached)
            && ReferenceEquals(cached.Api, api) && ReferenceEquals(cached.Material, liquid.Collectible))
            return cached.Color;
        // The atlas owns the average; do not reconstruct it from particle samples.
        int color = GetAverageColor(api, liquid.Collectible) ?? liquid.Collectible.GetRandomColor(api, liquid);
        Vector4 tint = color != 0
            ? new Vector4(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f,
                (color & 255) / 255f, fallback.W)
            : fallback;
        samples.Remove(vessel);
        samples.Add(vessel, new Sample(api, liquid.Collectible, tint));
        return tint;
    }
    #endregion

    #region Private
    /// <summary>Resolves the same baked particle texture used by the engine's standard item and block color APIs.</summary>
    private static int? GetAverageColor(ICoreClientAPI api, CollectibleObject material)
    {
        ITextureAtlasAPI atlas;
        int textureId;
        if (material is Item item)
        {
            if (item.Textures is null || item.Textures.Count == 0) return null;
            var texture = item.ParticlesTextureCode is { } code
                ? item.Textures.GetValueOrDefault(code) : item.Textures.First().Value;
            if (texture?.Baked is not { } baked) return null;
            textureId = baked.TextureSubId;
            atlas = api.ItemTextureAtlas;
        }
        else if (material is Block block)
        {
            textureId = block.TextureSubIdForBlockColor;
            atlas = api.BlockTextureAtlas;
        }
        else return null;
        return atlas is not null && textureId >= 0 ? atlas.GetAverageColor(textureId) : null;
    }

    /// <summary>Stores the borrowed material identity alongside its sampled tint.</summary>
    private sealed record Sample(ICoreClientAPI Api, CollectibleObject Material, Vector4 Color);
    #endregion
}
