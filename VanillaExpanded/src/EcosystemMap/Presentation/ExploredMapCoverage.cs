using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Reads only the live terrain components already used by vanilla's map renderer.</summary>
internal sealed class ExploredMapCoverage
{
    private static readonly FieldInfo loaded = typeof(ChunkMapLayer).GetField("loadedMapData", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("Terrain map coverage field changed.");

    #region Public API
    /// <summary>Snapshots explored chunks intersecting the view; absent, hidden or removed terrain never grants coverage.</summary>
    internal static HashSet<EcosystemTileKey> Capture(IEnumerable<MapLayer> layers, Cuboidd view)
    {
        var result = new HashSet<EcosystemTileKey>();
        foreach (var layer in layers)
        {
            if (layer is not ChunkMapLayer terrain || !terrain.Active) continue;
            if (loaded.GetValue(terrain) is not ConcurrentDictionary<FastVec2i, MultiChunkMapComponent> components)
                throw new NotSupportedException("Terrain map coverage layout changed.");
            // The component's chunk-set flags are authoritative, including unset and TTL removal.
            foreach (var component in components.Values)
            for (int z = 0; z < 3; z++)
            for (int x = 0; x < 3; x++)
            {
                int cx = component.chunkCoord.X + x, cz = component.chunkCoord.Y + z;
                if (cx * 32.0 >= view.X2 || (cx + 1) * 32.0 <= view.X1 || cz * 32.0 >= view.Z2 || (cz + 1) * 32.0 <= view.Z1) continue;
                if (component.IsChunkSet(x, z)) result.Add(new(cx, cz));
            }
        }
        return result;
    }
    #endregion
}
