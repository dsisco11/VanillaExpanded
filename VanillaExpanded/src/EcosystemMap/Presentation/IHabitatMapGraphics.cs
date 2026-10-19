using System;
using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Graphics-thread boundary for map resources; permits lifecycle tests without a separate graphics harness.</summary>
internal interface IHabitatMapGraphics : IDisposable
{
    /// <summary>Replaces the shader using the game's normal asset loader.</summary>
    bool Reload();
    /// <summary>Retains enough original textures for the current finite explored view.</summary>
    void SetView(int regions, int tiles);
    /// <summary>Acquires missing original textures before shared draw bindings are established.</summary>
    void Prepare(GpuHabitatReceipt receipt);
    /// <summary>Establishes one exception-safe graphics pass for all ready explored tiles.</summary>
    IDisposable Begin(GpuHabitatReceipt first, HabitatRanges ranges, float[] projection);
    /// <summary>Draws original inputs using exactly one criteria set and the current explored tile placement.</summary>
    void Draw(GpuHabitatReceipt receipt, HabitatMapTile tile);
    /// <summary>Discards disposable texture resources while leaving shader geometry reusable.</summary>
    void Clear();
}
