using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Animation;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Animation;

/// <summary>Checks reusable three-axis kinematics independently of liquid scaling, noise, and graphics.</summary>
[Trait("Category", "Unit")]
public sealed class ContainerMotionStateTests
{
    private readonly object world = new(), player = new(), camera = new();

    #region Public API
    /// <summary>Each physical axis supplies acceleration and braking history, including the backward axis liquids omit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Motion_PreservesAllAxesAndBraking(int axis)
    {
        var state = new ContainerMotionState();
        var direction = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        state.Update(0.02, Pose(Vector3.Zero));
        state.Update(0.02, Pose(Vector3.Zero));
        state.Update(0.02, Pose(direction * 0.02f));
        Assert.True(Vector3.Dot(state.WorldAcceleration, direction) > 0);
        Assert.Equal(state.WorldAcceleration, state.LocalAcceleration);
        for (int step = 2; step <= 10; step++) state.Update(0.02, Pose(direction * (step * 0.02f)));
        state.Update(0.02, Pose(direction * 0.2f));
        Assert.True(Vector3.Dot(state.LocalAcceleration, direction) < 0);
        Assert.True(Vector3.Dot(state.LocalVelocity, direction) > 0);
    }

    /// <summary>Entering a moving camera establishes velocity without manufacturing acceleration or liquid-specific units.</summary>
    [Fact]
    public void InitialVelocity_IsPublishedWithoutImpulse()
    {
        var state = new ContainerMotionState();
        state.Update(0.02, Pose(Vector3.Zero));
        state.Update(0.02, Pose(new(0, 0, 0.02f)));
        Assert.InRange(state.LocalVelocity.Z, 0.999f, 1.001f);
        Assert.Equal(Vector3.Zero, state.LocalAcceleration);
        Assert.True(state.HasPose);
        Assert.True(state.Update(0.02, null));
        Assert.False(state.HasPose);
        Assert.Equal(Vector3.Zero, state.WorldVelocity);
        Assert.Equal(Vector3.Zero, state.LocalAcceleration);
    }
    #endregion

    #region Private
    /// <summary>Builds a fixed-orientation camera sample with physical world displacement.</summary>
    private ItemSlotIndicatorCameraSample Pose(Vector3 displacement) =>
        new(world, player, camera, EnumCameraMode.FirstPerson,
            new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            Position: new(displacement.X, displacement.Y, displacement.Z));
    #endregion
}
