using VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;
using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Builds fixed grain corners and particle identities without per-frame uploads.</summary>
internal static class FoodGrainMeshGeometry
{
    #region Public API
    /// <summary>Creates one indexed quad per grain using the common vec3 position input.</summary>
    internal static MeshData Build()
    {
        int count = FoodGrainStateBuffers.ParticleCount;
        var mesh = new MeshData(false)
        {
            xyz = new float[count * 12],
            Indices = new int[count * 6],
            VerticesCount = count * 4,
            IndicesCount = count * 6,
            mode = EnumDrawMode.Triangles
        };
        // xy carries local corners; z indexes the shared simulation rather than GUI depth.
        for (int i = 0; i < count; i++)
        {
            for (int corner = 0; corner < 4; corner++)
            {
                int offset = i * 12 + corner * 3;
                mesh.xyz[offset] = corner is 1 or 2 ? 1 : -1;
                mesh.xyz[offset + 1] = corner >= 2 ? 1 : -1;
                mesh.xyz[offset + 2] = i;
            }
            int vertex = i * 4, index = i * 6;
            mesh.Indices[index] = vertex; mesh.Indices[index + 1] = vertex + 1; mesh.Indices[index + 2] = vertex + 2;
            mesh.Indices[index + 3] = vertex; mesh.Indices[index + 4] = vertex + 2; mesh.Indices[index + 5] = vertex + 3;
        }
        return mesh;
    }
    #endregion
}
