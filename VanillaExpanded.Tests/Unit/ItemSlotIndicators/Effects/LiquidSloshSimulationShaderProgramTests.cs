using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks typed solver inputs without creating a graphics context.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidSloshSimulationShaderProgramTests
{
    #region Public API
    #region Input Contracts
    /// <summary>Retained typed inputs can be configured independently of program activation or GPU allocation.</summary>
    [Fact]
    public void Properties_RetainCompleteSolverInputs()
    {
        var shader = new LiquidSloshSimulationShaderProgram
        {
            TimeStep = 1f / 240, ContainerAcceleration = new(-2, 3), Gravity = 4, Damping = 0,
            WallDamping = 6, WallDampingWidth = 0.2f,
            VerticalShapeVariation = 0.5f,
            CellCount = 16, CellSpacing = 0.125f, SourceStateTexture = 1,
            FeedbackBuffer = 2, FeedbackObject = 3, VertexArray = 4
        };
        Assert.IsAssignableFrom<ShaderProgram>(shader);
        Assert.Equal(1f / 240, shader.TimeStep);
        Assert.Equal(new Vector2(-2, 3), shader.ContainerAcceleration);
        Assert.Equal(4, shader.Gravity);
        Assert.Equal(0, shader.Damping);
        Assert.Equal(6, shader.WallDamping);
        Assert.Equal(0.2f, shader.WallDampingWidth);
        Assert.Equal(0.5f, shader.VerticalShapeVariation);
        Assert.Equal(16, shader.CellCount);
        Assert.Equal(0.125f, shader.CellSpacing);
        Assert.Equal(1, shader.SourceStateTexture);
        Assert.Equal(2, shader.FeedbackBuffer);
        Assert.Equal(3, shader.FeedbackObject);
        Assert.Equal(4, shader.VertexArray);
        Assert.Equal(8, LiquidSloshSimulationShaderProgram.StateStrideBytes);
    }

    /// <summary>Invalid floating-point inputs are rejected before any uniform submission.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidScalars_AreRejected(float invalid)
    {
        var shader = new LiquidSloshSimulationShaderProgram();
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.TimeStep = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.Gravity = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.CellSpacing = invalid);
        if (invalid != 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => shader.Damping = invalid);
            Assert.Throws<ArgumentOutOfRangeException>(() => shader.WallDamping = invalid);
            Assert.Throws<ArgumentOutOfRangeException>(() => shader.WallDampingWidth = invalid);
        }
        if (!float.IsFinite(invalid))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => shader.ContainerAcceleration = new(invalid, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => shader.ContainerAcceleration = new(0, invalid));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.TimeStep = 0.251f);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.WallDampingWidth = 0.501f);
    }

    /// <summary>Grid and borrowed handle limits cannot silently select absent resources or unsupported cell counts.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidGpuBindings_AreRejected(int invalid)
    {
        var shader = new LiquidSloshSimulationShaderProgram();
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.SourceStateTexture = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.FeedbackBuffer = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.FeedbackObject = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.VertexArray = invalid);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.CellCount = 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.CellCount = 65);
    }

    /// <summary>Advance rejects missing program, resources, and activation using managed checks before touching GPU state.</summary>
    [Fact]
    public void Advance_RequiresPreparedInputsAndCallerActivation()
    {
        var shader = new LiquidSloshSimulationShaderProgram();
        Assert.Equal("Compile the liquid solver before advancing it.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        // A synthetic handle lets the remaining guards run without allocating or binding a real program.
        shader.ProgramId = 1;
        Assert.Equal("The simulation must supply all borrowed GPU bindings before advancing.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        shader.SourceStateTexture = 1;
        shader.FeedbackBuffer = 2;
        shader.FeedbackObject = 3;
        shader.VertexArray = 4;
        Assert.Equal("Activate the liquid solver before advancing it.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
    }

    /// <summary>The explicit wave solver rejects unstable steps before activation checks or GPU work.</summary>
    [Fact]
    public void Advance_RejectsUnstableStepBeforeGpuSubmission()
    {
        var shader = new LiquidSloshSimulationShaderProgram
        {
            ProgramId = 1, SourceStateTexture = 1, FeedbackBuffer = 2, FeedbackObject = 3, VertexArray = 4,
            TimeStep = 0.1f, CellSpacing = 1f / 32, Gravity = 1
        };
        Assert.Equal("Liquid solver timestep exceeds the wave-propagation stability limit.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        // Reference gravity controls stability; a camera impulse does not change wave speed.
        shader.TimeStep = 1f / 120;
        shader.ContainerAcceleration = new(0, 3);
        Assert.Equal("Activate the liquid solver before advancing it.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        shader.Gravity = 4;
        Assert.Equal("Liquid solver timestep exceeds the wave-propagation stability limit.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        shader.TimeStep = 1f / 240;
        Assert.Equal("Activate the liquid solver before advancing it.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
    }
    /// <summary>The doubled grid's default timestep remains stable at both extremes of bounded container acceleration.</summary>
    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(3)]
    public void DoubledGrid_DefaultStepSupportsBoundedAcceleration(float vertical)
    {
        var shader = new LiquidSloshSimulationShaderProgram
        {
            ProgramId = 1, SourceStateTexture = 1, FeedbackBuffer = 2, FeedbackObject = 3, VertexArray = 4,
            ContainerAcceleration = new(0, vertical)
        };
        Assert.Equal(64, shader.CellCount);
        // Reaching the managed activation guard proves the real stability guard accepts the new default step.
        Assert.Equal("Activate the liquid solver before advancing it.",
            Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        if (vertical == 3)
        {
            shader.TimeStep = 1f / 120;
            Assert.Equal("Liquid solver timestep exceeds the wave-propagation stability limit.",
                Assert.Throws<InvalidOperationException>(() => shader.Advance()).Message);
        }
    }
    #endregion

    #region Shape Inputs
    /// <summary>Invalid shape weights cannot amplify forcing beyond the shader's bounded blend contract.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1.01f)]
    [InlineData(1.01f)]
    public void VerticalShapeVariation_RejectsInvalidWeights(float value)
    {
        var shader = new LiquidSloshSimulationShaderProgram();
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.VerticalShapeVariation = value);
    }
    #endregion

    #endregion
}
