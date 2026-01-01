using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock wrapper for IBlockAccessor that encapsulates Moq setup for unit testing.
/// </summary>
public class MockBlockAccessor
{
    public Mock<IBlockAccessor> Mock { get; }
    public IBlockAccessor Object => Mock.Object;

    private readonly Dictionary<BlockPos, BlockEntity> _blockEntities = new(BlockPosEqualityComparer.Instance);

    public MockBlockAccessor()
    {
        Mock = new Mock<IBlockAccessor>();
        SetupGetBlockEntity();
    }

    /// <summary>
    /// Registers a block entity at the specified position.
    /// </summary>
    public MockBlockAccessor WithBlockEntity(BlockPos pos, BlockEntity entity)
    {
        _blockEntities[pos] = entity;
        return this;
    }

    /// <summary>
    /// Registers a block entity at the specified coordinates.
    /// </summary>
    public MockBlockAccessor WithBlockEntity(int x, int y, int z, BlockEntity entity)
    {
        return WithBlockEntity(new BlockPos(x, y, z), entity);
    }

    /// <summary>
    /// Clears all registered block entities.
    /// </summary>
    public void ClearBlockEntities()
    {
        _blockEntities.Clear();
    }

    private void SetupGetBlockEntity()
    {
        Mock.Setup(b => b.GetBlockEntity(It.IsAny<BlockPos>()))
            .Returns((BlockPos pos) => _blockEntities.TryGetValue(pos, out var entity) ? entity : null);
    }

    /// <summary>
    /// Equality comparer for BlockPos to use as dictionary key.
    /// </summary>
    private class BlockPosEqualityComparer : IEqualityComparer<BlockPos>
    {
        public static readonly BlockPosEqualityComparer Instance = new();

        public bool Equals(BlockPos? x, BlockPos? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.X == y.X && x.Y == y.Y && x.Z == y.Z;
        }

        public int GetHashCode(BlockPos obj)
        {
            return HashCode.Combine(obj.X, obj.Y, obj.Z);
        }
    }
}
