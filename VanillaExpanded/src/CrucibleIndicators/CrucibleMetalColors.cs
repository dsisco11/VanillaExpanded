using System.Numerics;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CrucibleIndicators;

/// <summary>Caches a representative metal texture color so periodic provider samples do not flicker.</summary>
internal sealed class CrucibleMetalColors
{
    private readonly ConditionalWeakTable<CollectibleObject, Sample> samples = new();

    #region Public API
    /// <summary>Averages sixteen engine particle-color samples once per metal and client identity.</summary>
    internal Vector3 Resolve(ICoreClientAPI? client, ItemStack metal)
    {
        if (client is null) return new(0.55f, 0.57f, 0.6f);
        var sample = samples.GetValue(metal.Collectible, static _ => new Sample());
        if (ReferenceEquals(sample.Client, client)) return sample.Color;
        Vector3 total = default;
        for (int index = 0; index < 16; index++)
        {
            int packed = metal.Collectible.GetRandomColor(client, metal);
            total += packed == 0 ? new Vector3(0.55f, 0.57f, 0.6f)
                : new Vector3((packed >> 16 & 255) / 255f, (packed >> 8 & 255) / 255f, (packed & 255) / 255f);
        }
        sample.Client = client;
        sample.Color = total / 16;
        return sample.Color;
    }
    #endregion

    #region Private
    /// <summary>Retains one client identity and copied color for a weakly owned metal collectible.</summary>
    private sealed class Sample
    {
        internal ICoreClientAPI? Client;
        internal Vector3 Color;
    }
    #endregion
}
