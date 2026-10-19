using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>Captures the native terrain map's undecorated surface category from loaded client chunks only.</summary>
internal sealed class NativeOceanClassifier
{
    private readonly IBlockAccessor blocks;
    private readonly IList<Block> palette;
    private readonly byte[] categories;
    private readonly int oceanCategory, size;

    #region Public API
    /// <summary>Validates and snapshots the native resolved category table, including modded ocean blocks.</summary>
    internal NativeOceanClassifier(ChunkMapLayer terrain, IBlockAccessor blocks, IList<Block> palette, int size)
    {
        oceanCategory = terrain.colorsByCode.IndexOfKey("ocean");
        if (size != 32 || oceanCategory < 0 || oceanCategory > byte.MaxValue || terrain.block2Color == null
            || terrain.block2Color.Length != palette.Count)
            throw new NotSupportedException("Native terrain ocean classification layout changed.");
        this.blocks = blocks;
        this.palette = palette;
        this.size = size;
        categories = (byte[])terrain.block2Color.Clone();
    }

    /// <summary>Reads surface categories before shoreline decoration; texture/color-accurate rendering does not affect semantics.</summary>
    internal TileEnvironment? Capture(EcosystemTileKey key, IMapChunk map)
    {
        ushort[]? original = map.RainHeightMap;
        if (original == null || original.Length != size * size || key.Dimension != 0) return null;
        var heights = (ushort[])original.Clone();
        var column = new IWorldChunk[blocks.MapSizeY / size];
        // Match native availability: a complete normally loaded column, never remote lookup or generation.
        for (int y = 0; y < column.Length; y++)
        {
            var chunk = blocks.GetChunk(key.X, y, key.Z);
            if (chunk is not IClientChunk client || !client.LoadedFromServer || chunk.Disposed) return null;
            column[y] = chunk;
        }
        var values = new byte[heights.Length];
        for (int i = 0; i < values.Length; i++)
        {
            int y = heights[i];
            var block = Read(column, i % size, y, i / size);
            // Use the native parchment selection in all display modes: skip exactly one snow surface layer.
            if (block?.BlockMaterial == EnumBlockMaterial.Snow) block = Read(column, i % size, y - 1, i / size);
            if (block != null && block.Id > 0 && block.Id < categories.Length)
                values[i] = categories[block.Id] == oceanCategory ? (byte)2 : (byte)1;
        }
        if (!ReferenceEquals(original, map.RainHeightMap) || column.Any(chunk => chunk.Disposed)) return null;
        return new TileEnvironment(key, size, ocean: new OceanMask(size, values));
    }
    #endregion

    #region Private
    /// <summary>Uses vanilla's thread-safe fluid-or-solid read and treats unavailable/disposed surfaces as unknown.</summary>
    private Block? Read(IWorldChunk[] column, int x, int y, int z)
    {
        if (y < 0 || y / size >= column.Length || column[y / size].Disposed) return null;
        int id = column[y / size].UnpackAndReadBlock(((y % size) * size + z) * size + x, BlockLayersAccess.FluidOrSolid);
        return id > 0 && id < palette.Count ? palette[id] : null;
    }
    #endregion
}
