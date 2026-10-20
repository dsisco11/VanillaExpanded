using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks exact integer packing, native channel geometry and source availability without a graphics harness.</summary>
public sealed class PackedHabitatDataTests
{
    #region Public API
    /// <summary>Every native source retains its own layout and complete sample grid, including boundaries and density endpoints.</summary>
    [Fact]
    public void RegionalChannelsPreserveIndependentLayouts()
    {
        var climate = new EnvironmentalMap(20, 2, 2, Enumerable.Range(0, 400).Select(i => unchecked((int)0xFF000000) | ((i % 256) << 16) | (((255 - i) & 255) << 8) | 71));
        var forest = new EnvironmentalMap(17, 0, 1, Enumerable.Range(0, 289).Select(i => i % 256));
        var shrubs = new EnvironmentalMap(33, 0, 1, Enumerable.Range(0, 1089).Select(i => 255 - i % 256));
        var packed = new PackedRegionalData(new(default, climate, forest, shrubs));
        Assert.Equal(33, packed.Size);
        Assert.Equal(33 * 33 * 4, packed.Values.Length);
        Assert.Equal(new[] { 0, 2, 0 }, packed.Offsets);
        Assert.Same(climate, packed.Sources[0]);
        Assert.Same(forest, packed.Sources[1]);
        Assert.Same(shrubs, packed.Sources[2]);
        for (int z = 0; z < packed.Size; z++)
        for (int x = 0; x < packed.Size; x++)
        {
            int offset = (z * packed.Size + x) * 4;
            Assert.Equal(x < 20 && z < 20 ? (climate.Values[z * 20 + x] >> 16) & 255 : 0, packed.Values[offset]);
            Assert.Equal(x < 20 && z < 20 ? (climate.Values[z * 20 + x] >> 8) & 255 : 0, packed.Values[offset + 1]);
            Assert.Equal(x >= 2 && z >= 2 && x < 19 && z < 19 ? forest.Values[(z - 2) * 17 + x - 2] : 0, packed.Values[offset + 2]);
            Assert.Equal(shrubs.Values[z * 33 + x], packed.Values[offset + 3]);
        }
    }

    /// <summary>Native interpolation at chunk and region edges sees the same values after channel extraction.</summary>
    [Fact]
    public void NativeInterpolationRetainsSamplingBoundariesAndDensityThresholds()
    {
        var climate = new EnvironmentalMap(20, 2, 2, Enumerable.Range(0, 400).Select(i => ((i * 31 & 255) << 16) | ((i * 17 & 255) << 8) | (i & 255)));
        var forest = new EnvironmentalMap(17, 0, 1, Enumerable.Range(0, 289).Select(i => i % 2 == 0 ? 127 : 128));
        var shrubs = new EnvironmentalMap(33, 0, 1, Enumerable.Range(0, 1089).Select(i => i % 2 == 0 ? 0 : 255));
        var packed = new PackedRegionalData(new(default, climate, forest, shrubs));
        for (int component = 0; component < 3; component++)
        {
            var source = packed.Sources[component]!;
            int[] restored = Enumerable.Range(0, source.Values.Count).Select(i =>
            {
                int shift = packed.Offsets[component];
                int offset = ((i / source.Size + shift) * packed.Size + i % source.Size + shift) * 4;
                return component == 0 ? (packed.Values[offset] << 16) | (packed.Values[offset + 1] << 8) : packed.Values[offset + component + 1];
            }).ToArray();
            var original = NativeMap(source, source.Values.ToArray());
            var decoded = NativeMap(source, restored);
            foreach (float position in new[] { 0f, 15f / 512, 31f / 512, 32f / 512, 255f / 512, 511f / 512 })
            {
                int mask = component == 0 ? 0xFFFF00 : 0xFFFFFF;
                Assert.Equal(original.GetUnpaddedColorLerpedForNormalizedPos(position, position) & mask,
                    decoded.GetUnpaddedColorLerpedForNormalizedPos(position, position) & mask);
                if (component != 0)
                {
                    float expected = original.GetUnpaddedIntLerped(position * original.InnerSize, position * original.InnerSize) / 255f;
                    float actual = decoded.GetUnpaddedIntLerped(position * decoded.InnerSize, position * decoded.InnerSize) / 255f;
                    Assert.Equal(expected, actual);
                    Assert.Equal(expected >= .5f, actual >= .5f);
                }
            }
        }
    }

    /// <summary>Padding alignment preserves every boundary sample and uses the union extent without changing logical grids.</summary>
    [Fact]
    public void MatchingResolutionAlignsSampleOriginsAndRetainsPadding()
    {
        var climate = new EnvironmentalMap(6, 1, 1, Enumerable.Range(0, 36).Select(i => i << 16));
        var forest = new EnvironmentalMap(8, 3, 1, Enumerable.Range(0, 64));
        var shrubs = new EnvironmentalMap(10, 0, 2, Enumerable.Range(0, 100));
        var packed = new PackedRegionalData(new(default, climate, forest, shrubs));
        Assert.Equal(new[] { 2, 0, 0 }, packed.Offsets);
        Assert.Equal(climate.TopLeftPadding + packed.Offsets[0], forest.TopLeftPadding + packed.Offsets[1]);
        Assert.Equal(10, packed.Size);
        for (int z = 0; z < climate.Size; z++)
        for (int x = 0; x < climate.Size; x++)
            Assert.Equal((byte)(z * 6 + x), packed.Values[((z + 2) * packed.Size + x + 2) * 4]);
        var missingClimate = new PackedRegionalData(new(default, Forest: forest));
        Assert.Equal(0, missingClimate.Offsets[1]);
        Assert.Equal(8, missingClimate.Size);
        Assert.Null(missingClimate.Sources[0]);
    }

    /// <summary>Unexpected out-of-range density affects only that component and reports the unsupported input without truncation.</summary>
    [Fact]
    public void MissingAndInvalidComponentsNeverBecomeZeroDensity()
    {
        var climate = new EnvironmentalMap(2, 0, 1, new[] { int.MinValue, -1, int.MaxValue, 0 });
        var valid = new EnvironmentalMap(2, 0, 1, new[] { 0, 1, 254, 255 });
        foreach (int invalid in new[] { -1, 256, int.MaxValue })
        {
            var warnings = new List<string>();
            var bad = new EnvironmentalMap(2, 0, 1, new[] { 0, 1, invalid, 255 });
            var packed = new PackedRegionalData(new(default, climate, bad, valid), warnings.Add);
            Assert.Same(climate, packed.Sources[0]);
            Assert.Null(packed.Sources[1]);
            Assert.Same(valid, packed.Sources[2]);
            Assert.Single(warnings);
            Assert.Equal(invalid, bad.Values[2]);
        }
        var missing = new PackedRegionalData(null);
        Assert.Equal(1, missing.Size);
        Assert.All(missing.Sources, Assert.Null);
        Assert.Equal(new byte[4], missing.Values);
    }

    /// <summary>Every ushort height survives and category bits distinguish unknown, land and ocean independently of height availability.</summary>
    [Fact]
    public void TerrainPreservesEveryHeightBitAndIndependentFlags()
    {
        ushort[] heights = Enumerable.Range(0, 65536).Select(i => (ushort)i).ToArray();
        var ocean = new OceanMask(256, Enumerable.Range(0, 65536).Select(i => (byte)(i % 3)));
        var values = PackedTerrainData.Pack(new(default, 256, heights, ocean), 256);
        for (int i = 0; i < heights.Length; i++)
        {
            Assert.Equal(heights[i], values[i * 2]);
            Assert.Equal((ushort)(1 | ((i % 3) << 1)), values[i * 2 + 1]);
        }
        var categoriesOnly = PackedTerrainData.Pack(new(default, 256, ocean: ocean), 256);
        Assert.Equal((ushort)4, categoriesOnly[2 * 2 + 1]);
        Assert.Equal(new ushort[8], PackedTerrainData.Pack(new(default, 256, heights, ocean), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PackedTerrainData.Pack(null, 0));
    }
    #endregion

    #region Private
    /// <summary>Uses the game's own interpolation implementation as the reference for each original logical grid.</summary>
    private static IntDataMap2D NativeMap(EnvironmentalMap source, int[] values) => new()
    {
        Size = source.Size, TopLeftPadding = source.TopLeftPadding, BottomRightPadding = source.BottomRightPadding, Data = values
    };
    #endregion
}
