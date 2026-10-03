using System;

using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Builds fixed indexed triangles containing normalized horizontal coordinates and edge identity.</summary>
internal static class ItemSlotIndicatorMeshGeometry
{
    #region Public API
    /// <summary>Creates ABI-one geometry; bottom and surface vertices alternate at every sample.</summary>
    internal static MeshData Build(ItemSlotIndicatorMeshKey key)
    {
        if (key.AbiVersion != ItemSlotIndicatorEffectDefinition.CurrentAbiVersion
            || (key.Topology == ItemSlotIndicatorTopology.Quad ? key.SegmentCount != 1
                : key.Topology != ItemSlotIndicatorTopology.FillStrip || key.SegmentCount is < 2
                    or > ItemSlotIndicatorEffectDefinition.MaximumSegmentCount))
            throw new ArgumentException("Unsupported indicator mesh requirements.", nameof(key));

        int segments = key.SegmentCount;
        var mesh = new MeshData(false)
        {
            xyz = new float[6 * (segments + 1)],
            Indices = new int[6 * segments],
            VerticesCount = 2 * (segments + 1),
            IndicesCount = 6 * segments,
            mode = EnumDrawMode.Triangles
        };
        // Each adjacent pair shares its boundary vertices; no fill or animation data is baked into the mesh.
        for (int sample = 0; sample <= segments; sample++)
        {
            int vertex = 6 * sample;
            mesh.xyz[vertex] = mesh.xyz[vertex + 3] = (float)sample / segments;
            mesh.xyz[vertex + 4] = 1;
        }
        for (int segment = 0; segment < segments; segment++)
        {
            int bottom = 2 * segment;
            int index = 6 * segment;
            mesh.Indices[index] = bottom;
            mesh.Indices[index + 1] = bottom + 2;
            mesh.Indices[index + 2] = bottom + 3;
            mesh.Indices[index + 3] = bottom;
            mesh.Indices[index + 4] = bottom + 3;
            mesh.Indices[index + 5] = bottom + 1;
        }
        return mesh;
    }
    #endregion
}
