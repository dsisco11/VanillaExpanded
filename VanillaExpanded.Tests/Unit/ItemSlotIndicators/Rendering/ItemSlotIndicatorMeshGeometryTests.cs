using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Rendering;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks fixed-ABI geometry, triangle coverage, winding, and allocation bounds without a graphics context.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorMeshGeometryTests
{
    #region Public API
    /// <summary>Every segment covers exactly its unit-height interval with shared, consistently wound boundaries.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(64)]
    public void Geometry_CoversNormalizedRectangleWithBottomAndSurfacePairs(int segments)
    {
        var topology = segments == 1 ? ItemSlotIndicatorTopology.Quad : ItemSlotIndicatorTopology.FillStrip;
        var mesh = ItemSlotIndicatorMeshGeometry.Build(new(1, topology, segments));
        Assert.Equal(2 * (segments + 1), mesh.VerticesCount);
        Assert.Equal(6 * segments, mesh.IndicesCount);
        Assert.Equal(mesh.VerticesCount * 3, mesh.xyz.Length);
        Assert.Equal(mesh.IndicesCount, mesh.Indices.Length);
        Assert.Null(mesh.Uv);
        Assert.Null(mesh.Rgba);
        Assert.Null(mesh.Flags);
        Assert.Equal(EnumDrawMode.Triangles, mesh.mode);
        Assert.True(mesh.XyzStatic);

        // Edge identity is explicit: animation may move surface vertices but never the bottom samples.
        for (int sample = 0; sample <= segments; sample++)
            for (int edge = 0; edge <= 1; edge++)
            {
                int offset = (2 * sample + edge) * 3;
                Assert.Equal((float)sample / segments, mesh.xyz[offset]);
                Assert.Equal(edge, mesh.xyz[offset + 1]);
                Assert.Equal(0, mesh.xyz[offset + 2]);
            }
        double area = 0;
        for (int triangle = 0; triangle < mesh.IndicesCount; triangle += 3)
        {
            int a = mesh.Indices[triangle], b = mesh.Indices[triangle + 1], c = mesh.Indices[triangle + 2];
            Assert.All(new[] { a, b, c }, index => Assert.InRange(index, 0, mesh.VerticesCount - 1));
            double cross = (mesh.xyz[3 * b] - mesh.xyz[3 * a]) * (mesh.xyz[3 * c + 1] - mesh.xyz[3 * a + 1])
                - (mesh.xyz[3 * b + 1] - mesh.xyz[3 * a + 1]) * (mesh.xyz[3 * c] - mesh.xyz[3 * a]);
            Assert.True(cross > 0);
            area += cross / 2;
        }
        Assert.Equal(1, area, 6);
    }

    /// <summary>Invalid cache keys cannot bypass the geometry contract even if constructed independently of definitions.</summary>
    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 1, 65)]
    [InlineData(1, 9, 16)]
    public void Geometry_RejectsUnsupportedRequirements(int abi, int topology, int segments) =>
        Assert.Throws<ArgumentException>(() => ItemSlotIndicatorMeshGeometry.Build(new(abi, (ItemSlotIndicatorTopology)topology, segments)));
    #endregion
}
