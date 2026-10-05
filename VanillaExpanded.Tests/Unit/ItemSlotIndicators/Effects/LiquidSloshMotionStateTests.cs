using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks actual camera-force differentiation, signs, braking, and discontinuity resets without a GPU.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidSloshMotionStateTests
{
    private readonly object world = new(), player = new(), camera = new();

    #region Public API
    #region Translation and Inertia
    /// <summary>Metal retains signed inertia and braking while producing smaller, bounded impulses than water.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetalProfile_RestrainsMotionAndBraking(bool vertical)
    {
        var water = new LiquidSloshMotionState();
        var metal = new LiquidSloshMotionState(LiquidSloshSimulationProfile.Metal);
        foreach (var state in new[] { water, metal })
        {
            state.Update(0.02, Pose(0, 0));
            state.Update(0.02, Pose(0, 0));
        }
        for (int step = 1; step <= 10; step++)
        {
            var sample = Pose(vertical ? 0 : step * 0.02, vertical ? step * 0.02 : 0);
            water.Update(0.02, sample);
            metal.Update(0.02, sample);
            Assert.InRange(metal.ContainerAcceleration.Length(), 0, 1.20001f);
            Assert.Equal(water.VerticalShapeVariation * 0.15f, metal.VerticalShapeVariation, 6);
        }
        float drive = vertical ? metal.ContainerAcceleration.Y : metal.ContainerAcceleration.X;
        float waterDrive = vertical ? water.ContainerAcceleration.Y : water.ContainerAcceleration.X;
        Assert.True(drive > 0 && drive < waterDrive);
        var stop = Pose(vertical ? 0 : 0.2, vertical ? 0.2 : 0);
        water.Update(0.02, stop);
        metal.Update(0.02, stop);
        float braking = vertical ? metal.ContainerAcceleration.Y : metal.ContainerAcceleration.X;
        float waterBraking = vertical ? water.ContainerAcceleration.Y : water.ContainerAcceleration.X;
        Assert.True(braking < 0 && MathF.Abs(braking) < MathF.Abs(waterBraking));
        Assert.InRange(metal.ContainerAcceleration.Length(), 0, 1.20001f);
    }

    /// <summary>Translation acceleration and an abrupt stop produce opposite signed forces, even far from world origin.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TranslationAndBraking_ProduceOppositeForces(bool vertical)
    {
        var state = new LiquidSloshMotionState();
        Assert.True(state.Update(0.02, Pose(0, 0)));
        Assert.False(state.Update(0.02, Pose(0, 0)));
        for (int step = 1; step <= 10; step++)
            state.Update(0.02, Pose(vertical ? 0 : step * 0.02, vertical ? step * 0.02 : 0));
        float driven = vertical ? state.ContainerAcceleration.Y : state.ContainerAcceleration.X;
        Assert.True(driven > 0);
        state.Update(0.02, Pose(vertical ? 0 : 0.2, vertical ? 0.2 : 0));
        Assert.True((vertical ? state.ContainerAcceleration.Y : state.ContainerAcceleration.X) < 0);
        Assert.InRange(state.ContainerAcceleration.X, -3, 3);
        Assert.InRange(state.ContainerAcceleration.Y, -3, 3);
    }

    /// <summary>Rightward camera turns drive opposite-side fluid inertia and stopping the turn produces a reverse impulse.</summary>
    [Fact]
    public void TurnsAndStops_ProduceSignedInertia()
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        for (int step = 1; step <= 10; step++) state.Update(0.02, Pose(0, 0, step * 0.04f));
        Assert.True(state.ContainerAcceleration.X > 0);
        state.Update(0.02, Pose(0, 0, 0.4f));
        Assert.True(state.ContainerAcceleration.X < 0);
    }

    /// <summary>Constant straight-line speed loses its startup impulse instead of sustaining a procedural lean.</summary>
    [Fact]
    public void ConstantVelocity_SettlesToZeroAcceleration()
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        for (int step = 1; step <= 150; step++) state.Update(0.02, Pose(step * 0.02, 0));
        Assert.InRange(state.ContainerAcceleration.Length(), 0, 0.0001f);
    }

    #endregion

    #region Container Geometry
    /// <summary>Camera translation cancelling the rotated offset keeps the conceptual container still.</summary>
    [Fact]
    public void CombinedPose_WithStationaryContainer_HasNoAcceleration()
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        for (int step = 1; step <= 30; step++)
        {
            float yaw = step * 0.02f;
            var rotatedOffset = Vector3.Transform(new Vector3(0, -0.2f, -0.8f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw));
            var pose = Pose(0, 0, yaw) with
            {
                Position = new(1_000_000_000d - rotatedOffset.X, 0, -0.8 - rotatedOffset.Z)
            };
            state.Update(0.02, pose);
            Assert.InRange(state.ContainerAcceleration.Length(), 0, 0.0001f);
        }
    }

    /// <summary>Both pitch and roll move the offset container and produce acceleration without separate rotation factors.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PitchAndRoll_MoveConceptualContainer(bool roll)
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        var rotation = Quaternion.CreateFromAxisAngle(roll ? Vector3.UnitZ : Vector3.UnitX, 0.02f);
        var pose = Pose(0, 0) with
        {
            Basis = new(Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation),
                Vector3.Transform(Vector3.UnitZ, rotation))
        };
        state.Update(0.02, pose);
        Assert.True((roll ? state.ContainerAcceleration.X : state.ContainerAcceleration.Y) > 0);
    }

    #endregion

    #region Sampling and Limits
    /// <summary>Vertical-only motion creates small varying lateral impulses which settle with vertical acceleration.</summary>
    [Fact]
    public void VerticalJostle_IsBoundedAndSettlesWithoutIdleForcing()
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        float minimum = 0, maximum = 0;
        double height = 0;
        for (int step = 1; step <= 200; step++)
        {
            height = 0.02 * Math.Sin(step * 0.02 * 8);
            state.Update(0.02, Pose(0, height));
            var force = state.ContainerAcceleration;
            Assert.True(MathF.Abs(force.X) <= 0.08001f * MathF.Abs(force.Y));
            minimum = MathF.Min(minimum, force.X);
            maximum = MathF.Max(maximum, force.X);
        }
        Assert.True(minimum < -0.001f && maximum > 0.001f);
        for (int step = 0; step < 100; step++) state.Update(0.02, Pose(0, height));
        Assert.InRange(state.ContainerAcceleration.Length(), 0, 0.0001f);
    }

    /// <summary>Time-normalized differentiation converges on the same acceleration at different sampling rates.</summary>
    [Fact]
    public void ConstantAcceleration_IsConsistentAcrossFrameRates()
    {
        var fast = SampleAcceleration(0.01);
        var slow = SampleAcceleration(0.02);
        Assert.InRange(fast.X, 0.39f, 0.41f);
        Assert.InRange(Vector2.Distance(fast, slow), 0, 0.01f);
    }

    /// <summary>One magnitude limit retains the direction of a large combined translation impulse.</summary>
    [Fact]
    public void AccelerationLimit_PreservesCombinedDirection()
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0.1, 0.2));
        Assert.InRange(state.ContainerAcceleration.Length(), 2.999f, 3.001f);
        // The bounded vertical jostle changes the input direction by at most 8% of its vertical component.
        Assert.InRange(state.ContainerAcceleration.Y / state.ContainerAcceleration.X, 1.72f, 2.39f);
    }

    #endregion

    #region Discontinuities
    /// <summary>Context replacement, teleportation, missing poses, and frame gaps discard force and derivative history.</summary>
    [Theory]
    [InlineData("context")]
    [InlineData("teleport")]
    [InlineData("missing")]
    [InlineData("gap")]
    public void Discontinuities_ResetSharedForce(string kind)
    {
        var state = new LiquidSloshMotionState();
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0, 0));
        state.Update(0.02, Pose(0.02, 0));
        Assert.True(state.ContainerAcceleration.X > 0);
        ItemSlotIndicatorCameraSample? next = kind switch
        {
            "context" => Pose(0.02, 0) with { Camera = new object() },
            "teleport" => Pose(100, 0),
            "missing" => null,
            _ => Pose(0.02, 0)
        };
        Assert.True(state.Update(kind == "gap" ? 1 : 0.02, next));
        Assert.Equal(Vector2.Zero, state.ContainerAcceleration);
        Assert.Equal(0, state.VerticalShapeVariation);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Samples a one-second trajectory with constant two-blocks-per-second-squared acceleration.</summary>
    private Vector2 SampleAcceleration(double interval)
    {
        var state = new LiquidSloshMotionState();
        state.Update(interval, Pose(0, 0));
        for (int step = 1; step <= (int)(1 / interval); step++)
        {
            double time = step * interval;
            state.Update(interval, Pose(time * time, 0));
        }
        return state.ContainerAcceleration;
    }

    /// <summary>Copies a world position and camera basis, with positive yaw representing looking right.</summary>
    private ItemSlotIndicatorCameraSample Pose(double right, double up, float yaw = 0)
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -yaw);
        var basis = new ItemSlotIndicatorCameraBasis(Vector3.Transform(Vector3.UnitX, rotation),
            Vector3.UnitY, Vector3.Transform(Vector3.UnitZ, rotation));
        return new(world, player, camera, EnumCameraMode.FirstPerson, basis,
            Position: new(1_000_000_000 + right, up, 0));
    }
    #endregion
}
