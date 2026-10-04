using System;
using System.Numerics;

using OpenTK.Graphics.OpenGL4;

using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Owns typed liquid-solver inputs and transform-feedback submission through the engine shader program.</summary>
/// <remarks>The simulation owns the borrowed buffers, buffer texture, vertex array, and transform-feedback object.
/// Stage assets must supply the documented uniforms and one nextHeight/nextFlow pair per point invocation.</remarks>
internal sealed class LiquidSloshSimulationShaderProgram : ShaderProgram
{
    /// <summary>Size of an interleaved height/flow record in the caller-owned state buffers.</summary>
    internal const int StateStrideBytes = 2 * sizeof(float);
    /// <summary>Engine file-program basename for the paired liquid simulation stages.</summary>
    internal const string ShaderName = "vanillaexpanded_itemslot_liquid_simulation";
    private static readonly string[] feedbackVaryings = ["nextHeight", "nextFlow"];
    private static readonly string[] requiredUniforms =
        ["timeStep", "containerAcceleration", "verticalShapeVariation", "gravity", "damping", "wallDamping", "wallDampingWidth", "cellCount", "cellSpacing", "state"];
    private float timeStep = 1f / 480;
    private Vector2 containerAcceleration;
    private float verticalShapeVariation;
    private float gravity = 1, damping = 1, cellSpacing = 1f / LiquidSloshStateBuffers.CellCount;
    private float wallDamping, wallDampingWidth = 0.2f;
    private int cellCount = LiquidSloshStateBuffers.CellCount;
    private int sourceStateTexture, feedbackBuffer, feedbackObject, vertexArray;

    #region Public API
    #region Uniform Inputs
    #region Solver Forcing
    /// <summary>Gets or sets the positive solver step in seconds, bounded to a quarter second.</summary>
    internal float TimeStep
    {
        get => timeStep;
        set => timeStep = RequirePositive(value, nameof(TimeStep), 0.25f);
    }

    /// <summary>Gets or sets finite horizontal/vertical acceleration in the virtual container's coordinate system.</summary>
    internal Vector2 ContainerAcceleration
    {
        get => containerAcceleration;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
                throw new ArgumentOutOfRangeException(nameof(ContainerAcceleration));
            containerAcceleration = value;
        }
    }

    /// <summary>Gets or sets finite signed vertical-force shape variation between minus one and one.</summary>
    internal float VerticalShapeVariation
    {
        get => verticalShapeVariation;
        set
        {
            if (!float.IsFinite(value) || value < -1 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(VerticalShapeVariation));
            verticalShapeVariation = value;
        }
    }

    /// <summary>Gets or sets positive reference gravity controlling wave propagation in solver units.</summary>
    internal float Gravity
    {
        get => gravity;
        set => gravity = RequirePositive(value, nameof(Gravity));
    }

    #endregion

    #region Flow Damping
    /// <summary>Gets or sets finite nonnegative damping per second.</summary>
    internal float Damping
    {
        get => damping;
        set
        {
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(Damping));
            damping = value;
        }
    }

    /// <summary>Gets or sets additional nonnegative flow damping per second at the walls; zero disables it.</summary>
    internal float WallDamping
    {
        get => wallDamping;
        set
        {
            if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(WallDamping));
            wallDamping = value;
        }
    }

    /// <summary>Gets or sets the damping band width as a fraction of container width per wall, between zero and one half.</summary>
    internal float WallDampingWidth
    {
        get => wallDampingWidth;
        set
        {
            if (!float.IsFinite(value) || value < 0 || value > 0.5f)
                throw new ArgumentOutOfRangeException(nameof(WallDampingWidth));
            wallDampingWidth = value;
        }
    }

    #endregion

    #region Grid
    /// <summary>Gets or sets the fixed point count, between two and sixty-four cells.</summary>
    internal int CellCount
    {
        get => cellCount;
        set
        {
            if (value is < 2 or > 64) throw new ArgumentOutOfRangeException(nameof(CellCount));
            cellCount = value;
        }
    }

    /// <summary>Gets or sets positive horizontal cell spacing in solver units.</summary>
    internal float CellSpacing
    {
        get => cellSpacing;
        set => cellSpacing = RequirePositive(value, nameof(CellSpacing));
    }
    #endregion
    #endregion

    #region Borrowed GPU Inputs
    /// <summary>Gets or sets the buffer-texture name exposing the read-only previous state to samplerBuffer state.</summary>
    internal int SourceStateTexture
    {
        get => sourceStateTexture;
        set => sourceStateTexture = RequireHandle(value, nameof(SourceStateTexture));
    }

    /// <summary>Gets or sets the destination buffer, sized for CellCount interleaved displacement/right-face-flow records.</summary>
    /// <remarks>It must not back SourceStateTexture; ping-pong ownership and capacity belong to the simulation.</remarks>
    internal int FeedbackBuffer
    {
        get => feedbackBuffer;
        set => feedbackBuffer = RequireHandle(value, nameof(FeedbackBuffer));
    }

    /// <summary>Gets or sets the dedicated transform-feedback object whose binding zero receives the next state.</summary>
    internal int FeedbackObject
    {
        get => feedbackObject;
        set => feedbackObject = RequireHandle(value, nameof(FeedbackObject));
    }

    /// <summary>Gets or sets the solver vertex array; the vertex shader indexes cells with gl_VertexID.</summary>
    internal int VertexArray
    {
        get => vertexArray;
        set => vertexArray = RequireHandle(value, nameof(VertexArray));
    }
    #endregion

    #region Compilation and Execution
    /// <summary>Configures feedback before the engine links, preserving engine diagnostics and fresh uniform locations.</summary>
    public override bool Compile()
    {
        if (GeometryShader is not null)
            throw new InvalidOperationException("The liquid solver uses one vertex invocation per cell, without a geometry stage.");
        // The scoped adapter only changes linking for this concrete program and is removed even on failure.
        bool compiled = LiquidSloshTransformFeedbackLink.Compile(this, () => base.Compile());
        if (!compiled) return false;
        try
        {
            ItemSlotIndicatorBufferSampler.Register(this, "state");
            foreach (string uniform in requiredUniforms)
                if (!HasUniform(uniform))
                    throw new InvalidOperationException($"Liquid solver is missing uniform '{uniform}'.");
            ValidateLinkedInputs();
            return true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Installs the interleaved feedback record while the engine program is created but not yet linked.</summary>
    internal void ConfigureFeedback(int programId) =>
        GL.TransformFeedbackVaryings(programId, feedbackVaryings.Length, feedbackVaryings, TransformFeedbackMode.InterleavedAttribs);

    /// <summary>Advances the fixed point grid in the dedicated simulation pass without rasterization or CPU readback.</summary>
    /// <remarks>The caller activates this program before submission and stops it in its own finally block.
    /// Enter with no active transform feedback and rasterizer discard disabled. This method returns with
    /// feedback ended, discard disabled, scratch bindings released, and texture unit zero selected.</remarks>
    internal void Advance()
    {
        if (Disposed || ProgramId <= 0) throw new InvalidOperationException("Compile the liquid solver before advancing it.");
        if (sourceStateTexture == 0 || feedbackBuffer == 0 || feedbackObject == 0 || vertexArray == 0)
            throw new InvalidOperationException("The simulation must supply all borrowed GPU bindings before advancing.");
        ValidateSolverStep();
        if (!ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, this))
            throw new InvalidOperationException("Activate the liquid solver before advancing it.");

        bool feedbackBegun = false;
        try
        {
            SubmitInputs();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.TextureBuffer, sourceStateTexture);
            GL.BindVertexArray(vertexArray);
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedbackObject);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, feedbackBuffer);
            GL.Enable(EnableCap.RasterizerDiscard);
            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points);
            feedbackBegun = true;
            GL.DrawArrays(PrimitiveType.Points, 0, cellCount);
        }
        finally
        {
            try
            {
                if (feedbackBegun) GL.EndTransformFeedback();
            }
            finally
            {
                // Release only our scratch bindings. The following render pass owns its shader and mesh state;
                // arbitrary prior bindings are outside this simulation boundary's contract.
                GL.Disable(EnableCap.RasterizerDiscard);
                GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0);
                GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, 0);
                GL.BindVertexArray(0);
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.TextureBuffer, 0);
            }
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Rejects unstable explicit steps using the solver's reference-depth wave speed before submission.</summary>
    private void ValidateSolverStep()
    {
        // Camera acceleration excites the fluid but never changes its propagation speed.
        // Double arithmetic avoids overflow in the reference-depth wave-speed guard.
        double courant = timeStep * Math.Sqrt(gravity) / cellSpacing;
        if (courant > 0.45)
            throw new InvalidOperationException("Liquid solver timestep exceeds the wave-propagation stability limit.");
    }

    /// <summary>Rejects linked inputs or captured records that cannot match the typed setters and eight-byte state layout.</summary>
    private void ValidateLinkedInputs()
    {
        GL.GetProgram(ProgramId, GetProgramParameterName.ActiveUniforms, out int count);
        for (int index = 0; index < count; index++)
        {
            string name = GL.GetActiveUniform(ProgramId, index, out int size, out ActiveUniformType type);
            ActiveUniformType? expected = name switch
            {
                "timeStep" or "gravity" or "damping" or "wallDamping" or "wallDampingWidth" or "verticalShapeVariation" or "cellSpacing" => ActiveUniformType.Float,
                "containerAcceleration" => ActiveUniformType.FloatVec2,
                "cellCount" => ActiveUniformType.Int,
                "state" => ActiveUniformType.SamplerBuffer,
                _ => null
            };
            if (expected is { } required && (size != 1 || type != required))
                throw new InvalidOperationException($"Liquid solver uniform '{name}' has an incompatible type.");
        }
        GL.GetProgram(ProgramId, GetProgramParameterName.TransformFeedbackVaryings, out int varyingCount);
        if (varyingCount != feedbackVaryings.Length)
            throw new InvalidOperationException("Liquid feedback must capture exactly two scalar fields.");
        // Validate linked field types rather than assuming output declarations fit the destination stride.
        for (int index = 0; index < varyingCount; index++)
        {
            GL.GetTransformFeedbackVarying(ProgramId, index, 64, out _, out int size,
                out TransformFeedbackType type, out string name);
            if (size != 1 || type != TransformFeedbackType.Float || name != feedbackVaryings[index])
                throw new InvalidOperationException("Liquid feedback must contain scalar nextHeight followed by scalar nextFlow.");
        }
    }

    /// <summary>Publishes the complete retained input set only after activating the program.</summary>
    private void SubmitInputs()
    {
        Uniform("timeStep", timeStep);
        Uniform("containerAcceleration", containerAcceleration.X, containerAcceleration.Y);
        Uniform("verticalShapeVariation", verticalShapeVariation);
        Uniform("gravity", gravity);
        Uniform("damping", damping);
        Uniform("wallDamping", wallDamping);
        Uniform("wallDampingWidth", wallDampingWidth);
        Uniform("cellCount", cellCount);
        Uniform("cellSpacing", cellSpacing);
        Uniform("state", 0);
    }

    /// <summary>Rejects invalid positive scalar inputs before they reach GPU state.</summary>
    private static float RequirePositive(float value, string name, float maximum = float.MaxValue)
    {
        if (!float.IsFinite(value) || value <= 0 || value > maximum) throw new ArgumentOutOfRangeException(name);
        return value;
    }

    /// <summary>Rejects absent or negative borrowed OpenGL names without allocating resources.</summary>
    private static int RequireHandle(int value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name);
        return value;
    }
    #endregion
}
