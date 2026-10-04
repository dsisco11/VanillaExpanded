using System.Numerics;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;
using VanillaExpanded.ItemSlotIndicators.Rendering;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks grain state initialization, fixed mesh identity, and typed solver guards.</summary>
[Trait("Category", "Unit")]
public sealed class FoodGrainIndicatorEffectTests
{
    #region Public API
    /// <summary>Initial grains are finite, inside walls, nonoverlapping, and at rest.</summary>
    [Fact]
    public void InitialState_IsSeparatedAndAtRest()
    {
        var state = FoodGrainStateBuffers.CreateInitialState();
        Assert.Equal(FoodGrainStateBuffers.ParticleCount * 8, state.Length);
        Assert.Equal(state, FoodGrainStateBuffers.CreateInitialState());
        var xCoordinates = new HashSet<float>();
        var yCoordinates = new HashSet<float>();
        for (int i = 0; i < FoodGrainStateBuffers.ParticleCount; i++)
        {
            int offset = 8 * i;
            float radius = state[offset + 6];
            Assert.InRange(radius, 0.01f, 0.023f);
            xCoordinates.Add(state[offset]);
            yCoordinates.Add(state[offset + 1]);
            Assert.InRange(state[offset], radius, 1 - radius);
            Assert.InRange(state[offset + 1], radius, 1 - radius);
            Assert.Equal(0, state[offset + 2]);
            Assert.Equal(0, state[offset + 3]);
            Assert.Equal(state[offset], state[offset + 4]);
            Assert.Equal(state[offset + 1], state[offset + 5]);
            for (int j = 0; j < i; j++)
            {
                float distance = Vector2.Distance(new(state[offset], state[offset + 1]), new(state[j * 8], state[j * 8 + 1]));
                Assert.True(distance >= radius + state[j * 8 + 6]);
            }
        }
        // Rows and columns would repeat coordinates; random placement should have essentially unique axes.
        Assert.True(xCoordinates.Count > FoodGrainStateBuffers.ParticleCount * 0.99f);
        Assert.True(yCoordinates.Count > FoodGrainStateBuffers.ParticleCount * 0.99f);
    }

    /// <summary>Every grain has exactly one reusable quad addressing its shared particle record.</summary>
    [Fact]
    public void Mesh_AddressesAllSharedGrains()
    {
        var definition = FoodGrainIndicatorEffect.Definition;
        Assert.True(definition.NeedsCameraMotion);
        Assert.True(definition.DrawBackground);
        var mesh = ItemSlotIndicatorMeshGeometry.Build(ItemSlotIndicatorMeshKey.From(definition));
        Assert.Equal(FoodGrainStateBuffers.ParticleCount * 4, mesh.VerticesCount);
        Assert.Equal(FoodGrainStateBuffers.ParticleCount * 6, mesh.IndicesCount);
        for (int i = 0; i < mesh.VerticesCount; i++)
        {
            Assert.Equal(i / 4, mesh.xyz[i * 3 + 2]);
            Assert.InRange(mesh.xyz[i * 3], -1, 1);
            Assert.InRange(mesh.xyz[i * 3 + 1], -1, 1);
        }
        Assert.All(mesh.Indices, index => Assert.InRange(index, 0, mesh.VerticesCount - 1));
    }

    /// <summary>Invalid solver inputs are rejected before touching the graphics context.</summary>
    [Fact]
    public void Shader_RejectsInvalidInputsAndUnpreparedSubmission()
    {
        var shader = new FoodGrainSimulationShaderProgram();
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.TimeStep = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.TimeStep = 0.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.Pass = 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.ContainerAcceleration = new(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.SourceStateTexture = 0);
        Assert.Throws<InvalidOperationException>(() => shader.Advance());
    }
    #endregion
}
