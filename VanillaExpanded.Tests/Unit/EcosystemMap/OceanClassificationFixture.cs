using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.EcosystemMap.Acquisition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Provides native terrain categories and recorded loaded-chunk reads without rendering or loading a world.</summary>
internal sealed class OceanClassificationFixture
{
    internal Mock<IBlockAccessor> Blocks { get; } = new(MockBehavior.Strict);
    internal Mock<IMapChunk> Map { get; } = new();
    internal Mock<IClientChunk>[] Chunks { get; } = { new(), new() };
    internal ChunkMapLayer Terrain { get; } = (ChunkMapLayer)RuntimeHelpers.GetUninitializedObject(typeof(ChunkMapLayer));
    internal List<Block> Palette { get; } = new();
    internal ushort[] Heights { get; } = Enumerable.Repeat((ushort)32, 1024).ToArray();
    internal System.Func<int, int, int, int> Surface { get; set; } = (_, _, _) => 2;
    internal List<int> ReadThreads { get; } = new();
    internal NativeOceanClassifier Classifier { get; }

    #region Public API
    /// <summary>Assigns identical RGB values to ocean/lake/lava so only the semantic lookup can distinguish them.</summary>
    internal OceanClassificationFixture()
    {
        Terrain.colorsByCode = new();
        foreach (string category in new[] { "land", "lake", "ocean", "lava", "glacier" }) Terrain.colorsByCode.Add(category, 0xCCC890);
        var materials = new[] { EnumBlockMaterial.Air, EnumBlockMaterial.Stone, EnumBlockMaterial.Water,
            EnumBlockMaterial.Water, EnumBlockMaterial.Lava, EnumBlockMaterial.Snow, EnumBlockMaterial.Stone };
        for (int i = 0; i < materials.Length; i++) Palette.Add(new Block { BlockId = i, BlockMaterial = materials[i] });
        Terrain.block2Color = new byte[] { 0, 0, 2, 1, 3, 4, 2 };
        Map.SetupGet(value => value.RainHeightMap).Returns(Heights);
        Blocks.SetupGet(value => value.MapSizeY).Returns(64);
        for (int level = 0; level < Chunks.Length; level++)
        {
            int y = level;
            Chunks[y].SetupGet(value => value.LoadedFromServer).Returns(true);
            Chunks[y].Setup(value => value.UnpackAndReadBlock(It.IsAny<int>(), BlockLayersAccess.FluidOrSolid))
                .Returns((int index, int layer) =>
                {
                    ReadThreads.Add(Environment.CurrentManagedThreadId);
                    return Surface(index % 32, index / 1024 + y * 32, index / 32 % 32);
                });
            Blocks.Setup(value => value.GetChunk(It.IsAny<int>(), y, It.IsAny<int>())).Returns(Chunks[y].Object);
        }
        Classifier = new(Terrain, Blocks.Object, Palette, 32);
    }
    #endregion
}
