using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns an immutable set of food-particle RGB samples for allocation-free shader submission.</summary>
internal sealed class ItemSlotIndicatorParticlePalette
{
    /// <summary>Fixed palette length matching the grain shader's uniform array.</summary>
    internal const int ColorCount = 16;
    private readonly float[] values = new float[ColorCount * 4];
    /// <summary>Gets the neutral food colors used when no client texture palette is available.</summary>
    internal static ItemSlotIndicatorParticlePalette Default { get; } = new(new int[ColorCount]);

    #region Public API
    /// <summary>Copies packed engine colors, replacing unavailable zero samples with a neutral grain color.</summary>
    internal ItemSlotIndicatorParticlePalette(ReadOnlySpan<int> colors)
    {
        if (colors.Length != ColorCount) throw new ArgumentException("Particle palettes require sixteen colors.", nameof(colors));
        for (int i = 0; i < ColorCount; i++)
        {
            int color = colors[i] == 0 ? unchecked((int)0xFFE3CC9B) : colors[i];
            values[i * 4] = ((color >> 16) & 255) / 255f;
            values[i * 4 + 1] = ((color >> 8) & 255) / 255f;
            values[i * 4 + 2] = (color & 255) / 255f;
            // Particle opacity belongs to the indicator; atlas sample alpha must not hide food colors.
            values[i * 4 + 3] = 1;
        }
    }

    /// <summary>Submits retained palette storage through the engine without allocating a per-draw array.</summary>
    internal void Submit(IShaderProgram program) => program.Uniforms4("foodPalette", ColorCount, values);
    #endregion
}
