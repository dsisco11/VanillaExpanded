using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Animation;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Animation;

/// <summary>Checks bounded shared animation, rotation signs, damping, wrapping, and reset behavior with controlled inputs.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorFrameStateTests
{
    private readonly object world = new(), player = new(), camera = new();

    #region Public API
    #region Time and Snapshots
    /// <summary>Absolute monotonic time advances periodic GUI animation with no world or camera dependency.</summary>
    [Fact]
    public void TimeOnly_UsesAcceptedElapsedTimeAndReadOnlySnapshots()
    {
        var state = new ItemSlotIndicatorFrameState();
        Assert.Equal(default, state.Snapshot);
        state.Update(1_000_000, false, null);
        state.Update(1_000_000.1, false, null);
        var snapshot = state.Snapshot;
        Assert.Equal(0.1f, snapshot.TimeSeconds, 5);
        Assert.Equal(Vector2.Zero, snapshot.Motion);
        for (int draw = 0; draw < 250; draw++) Assert.Equal(snapshot, state.Snapshot);
        // Long suppressed-overlay intervals are excluded rather than accumulated into a later GUI frame.
        state.Update(1_000_100, false, null);
        Assert.Equal(snapshot, state.Snapshot);
        state.Update(1_000_100.1, false, null);
        Assert.Equal(0.2f, state.Snapshot.TimeSeconds, 5);
    }

    /// <summary>Bounded double accumulation preserves a periodic clock over many complete float publication cycles.</summary>
    [Fact]
    public void PeriodicTime_WrapsAt64WithoutPublishing64()
    {
        var state = new ItemSlotIndicatorFrameState();
        state.Update(0, false, null);
        for (int tick = 1; tick <= 512; tick++) state.Update(tick * 0.125, false, null);
        Assert.Equal(0, state.Snapshot.TimeSeconds);
        double now = 64;
        for (int tick = 1; tick <= 511; tick++) state.Update(now += 0.125, false, null);
        state.Update(now += 0.1249999, false, null);
        Assert.InRange(state.Snapshot.TimeSeconds, 63.99f, MathF.BitDecrement(64));
        state.Update(now += 0.125, false, null);
        Assert.InRange(state.Snapshot.TimeSeconds, 0.124f, 0.126f);
    }

    /// <summary>Invalid and discontinuous clock readings freeze animation and discard camera impulses.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(0.1)]
    [InlineData(1)]
    public void InvalidOrLongElapsedTime_ResetsMotionWithoutCatchUp(double next)
    {
        var state = MovingState();
        state.Update(next, true, Sample(Rotation(Vector3.UnitY, -0.8f)));
        Assert.Equal(0.1f, state.Snapshot.TimeSeconds, 5);
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
        // Reestablish a finite clock after NaN/infinity before checking recovery.
        double baseline = double.IsFinite(next) ? next : 10;
        if (!double.IsFinite(next)) state.Update(baseline, true, Sample(Rotation(Vector3.UnitY, -0.8f)));
        state.Update(baseline + 0.1, true, Sample(Rotation(Vector3.UnitY, -0.8f)));
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
        Assert.Equal(0.2f, state.Snapshot.TimeSeconds, 5);
    }
    #endregion

    #region Rotation and Damping
    /// <summary>Camera-local axes produce positive right/up signals and ignore pure roll.</summary>
    [Theory]
    [InlineData(0, -0.2f, 1, 0)]
    [InlineData(0, 0.2f, -1, 0)]
    [InlineData(1, 0.2f, 0, 1)]
    [InlineData(1, -0.2f, 0, -1)]
    [InlineData(2, 0.2f, 0, 0)]
    public void ControlledRotations_UseRightwardAndUpwardLookRates(int axis, float angle, int signX, int signY)
    {
        var state = new ItemSlotIndicatorFrameState();
        state.Update(0, true, Sample(Identity));
        Vector3 rotationAxis = axis == 0 ? Vector3.UnitY : axis == 1 ? Vector3.UnitX : Vector3.UnitZ;
        state.Update(0.1, true, Sample(Rotation(rotationAxis, angle)));
        float expected = 0.5f * (float)(1 - Math.Exp(-0.1 / 0.12));
        Assert.Equal(signX * expected, state.Snapshot.Motion.X, 5);
        Assert.Equal(signY * expected, state.Snapshot.Motion.Y, 5);
    }

    /// <summary>Equivalent constant-rate motion has the same damping response at different frame rates.</summary>
    [Fact]
    public void Damping_IsFrameRateIndependentAndDecaysAfterMotionStops()
    {
        var slow = new ItemSlotIndicatorFrameState();
        var fast = new ItemSlotIndicatorFrameState();
        slow.Update(0, true, Sample(Identity));
        fast.Update(0, true, Sample(Identity));
        for (int frame = 1; frame <= 5; frame++) slow.Update(frame * 0.1, true, Sample(Rotation(Vector3.UnitY, -frame * 0.2f)));
        for (int frame = 1; frame <= 50; frame++) fast.Update(frame * 0.01, true, Sample(Rotation(Vector3.UnitY, -frame * 0.02f)));
        Assert.Equal(slow.Snapshot.Motion.X, fast.Snapshot.Motion.X, 4);
        float prior = slow.Snapshot.Motion.X;
        slow.Update(0.6, true, Sample(Rotation(Vector3.UnitY, -1)));
        Assert.Equal(prior * (float)Math.Exp(-0.1 / 0.12), slow.Snapshot.Motion.X, 5);
    }

    /// <summary>Shortest-arc rotation crosses angular wrapping and remains valid near pitched camera poles.</summary>
    [Fact]
    public void WrappingAndPitchedBasis_AvoidEulerDiscontinuities()
    {
        var state = new ItemSlotIndicatorFrameState();
        state.Update(0, true, Sample(Rotation(Vector3.UnitY, MathF.PI - 0.01f)));
        state.Update(0.1, true, Sample(Rotation(Vector3.UnitY, -MathF.PI + 0.01f)));
        Assert.InRange(state.Snapshot.Motion.X, -0.03f, -0.02f);
        var pitched = Rotation(Vector3.UnitX, MathF.PI / 2 - 0.0001f);
        state = new();
        state.Update(0, true, Sample(pitched));
        // Rotate about the previous camera's own up axis, rather than the fixed world axis.
        state.Update(0.1, true, Sample(RotateBasis(pitched, pitched.Up, -0.2f)));
        Assert.InRange(state.Snapshot.Motion.X, 0.28f, 0.29f);
        Assert.Equal(0, state.Snapshot.Motion.Y, 5);
    }

    /// <summary>Normalized rates clamp before smoothing, while a large orientation jump establishes a neutral baseline.</summary>
    [Fact]
    public void RateBoundsAndLargeTurns_DoNotProduceUnboundedImpulses()
    {
        var state = new ItemSlotIndicatorFrameState();
        state.Update(0, true, Sample(Identity));
        state.Update(0.01, true, Sample(Rotation(Vector3.UnitY, -0.5f)));
        Assert.Equal((float)(1 - Math.Exp(-0.01 / 0.12)), state.Snapshot.Motion.X, 5);
        state.Update(0.1, true, Sample(Rotation(Vector3.UnitY, 2)));
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
        state.Update(0.2, true, Sample(Rotation(Vector3.UnitY, 2)));
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
    }
    #endregion

    #region Resets and Camera Input
    /// <summary>Every authoritative context replacement resets motion without suspending time-only animation.</summary>
    [Theory]
    [InlineData("world")]
    [InlineData("player")]
    [InlineData("camera")]
    [InlineData("mode")]
    public void ContextReplacement_ResetsWithoutAnImpulse(string changed)
    {
        var state = MovingState();
        var sample = Sample(Rotation(Vector3.UnitY, -1));
        sample = changed switch
        {
            "world" => sample with { World = new object() },
            "player" => sample with { Player = new object() },
            "camera" => sample with { Camera = new object() },
            _ => sample with { Mode = EnumCameraMode.ThirdPerson }
        };
        state.Update(0.2, true, sample);
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
        Assert.Equal(0.2f, state.Snapshot.TimeSeconds, 5);
        state.Update(0.3, true, sample);
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
    }

    /// <summary>Missing, malformed, and inactive camera inputs discard the prior baseline; returning cameras begin neutral.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("nan")]
    [InlineData("scaled")]
    [InlineData("skewed")]
    [InlineData("reflected")]
    [InlineData("inactive")]
    public void InvalidOrInactiveCamera_ResetsAndReactivatesNeutral(string kind)
    {
        var state = MovingState();
        var basis = kind switch
        {
            "nan" => Identity with { Right = new(float.NaN, 0, 0) },
            "scaled" => Identity with { Right = Vector3.UnitX * 2 },
            "skewed" => Identity with { Up = Vector3.UnitX },
            "reflected" => Identity with { Backward = -Vector3.UnitZ },
            _ => Identity
        };
        state.Update(0.2, kind != "inactive", kind == "missing" ? null : Sample(basis));
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
        Assert.Equal(0.2f, state.Snapshot.TimeSeconds, 5);
        state.Update(0.3, true, Sample(Rotation(Vector3.UnitY, -1)));
        Assert.Equal(Vector2.Zero, state.Snapshot.Motion);
    }

    /// <summary>The matrix adapter copies rows, ignores translation, and retains no mutable engine array.</summary>
    [Fact]
    public void ViewMatrixBasis_CopiesCameraAxesWithoutTranslation()
    {
        var expected = Rotation(Vector3.UnitY, 0.4f);
        double[] matrix = [expected.Right.X, expected.Up.X, expected.Backward.X, 0,
            expected.Right.Y, expected.Up.Y, expected.Backward.Y, 0,
            expected.Right.Z, expected.Up.Z, expected.Backward.Z, 0, double.NaN, 1e10, 40, 1];
        var copied = ItemSlotIndicatorCameraBasis.FromViewMatrix(matrix);
        Assert.Equal(expected, copied);
        Assert.True(copied.IsValid());
        matrix[0] = double.NaN;
        Assert.True(copied.IsValid());
        Assert.False(ItemSlotIndicatorCameraBasis.FromViewMatrix(matrix).IsValid());
        Assert.False(ItemSlotIndicatorCameraBasis.FromViewMatrix([]).IsValid());
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Gets an identity right-handed camera basis facing negative Z.</summary>
    private static ItemSlotIndicatorCameraBasis Identity => new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>Creates a sample sharing this test's fixed authoritative context.</summary>
    private ItemSlotIndicatorCameraSample Sample(ItemSlotIndicatorCameraBasis basis) => new(world, player, camera, EnumCameraMode.FirstPerson, basis);

    /// <summary>Establishes a baseline and a nonzero rightward motion response.</summary>
    private ItemSlotIndicatorFrameState MovingState()
    {
        var state = new ItemSlotIndicatorFrameState();
        state.Update(0, true, Sample(Identity));
        state.Update(0.1, true, Sample(Rotation(Vector3.UnitY, -0.2f)));
        Assert.True(state.Snapshot.Motion.X > 0);
        return state;
    }

    /// <summary>Produces a controlled orientation from axis-angle rotation.</summary>
    private static ItemSlotIndicatorCameraBasis Rotation(Vector3 axis, float angle) => RotateBasis(Identity, axis, angle);

    /// <summary>Rotates all axes together, preserving a right-handed orthonormal basis.</summary>
    private static ItemSlotIndicatorCameraBasis RotateBasis(ItemSlotIndicatorCameraBasis basis, Vector3 axis, float angle)
    {
        var rotation = Quaternion.CreateFromAxisAngle(axis, angle);
        return new(Vector3.Transform(basis.Right, rotation), Vector3.Transform(basis.Up, rotation), Vector3.Transform(basis.Backward, rotation));
    }
    #endregion
}
