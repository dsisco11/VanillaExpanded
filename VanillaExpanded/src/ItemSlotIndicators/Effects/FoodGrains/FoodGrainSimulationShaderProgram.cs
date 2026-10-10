using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;

/// <summary>Owns typed particle-solver inputs and transform-feedback execution through engine shader tracking.</summary>
internal sealed class FoodGrainSimulationShaderProgram : ShaderProgram
{
    /// <summary>Engine basename for particle simulation stages.</summary>
    internal const string ShaderName = "vanillaexpanded_itemslot_food_simulation";
    private static readonly string[] varyings = ["nextPositionVelocity", "nextPreviousRadius"];
    private float timeStep = 1f / 120;
    private Vector2 acceleration;
    private int pass, source, destination, feedback, vertexArray;
    private int particleCount = FoodGrainStateBuffers.ParticleCount;

    #region Public API
    #region Uniform Inputs
    /// <summary>Gets or sets a positive fixed step of at most one hundred twentieth second.</summary>
    internal float TimeStep
    {
        get => timeStep;
        set
        {
            if (!float.IsFinite(value) || value <= 0 || value > 1f / 120) throw new ArgumentOutOfRangeException(nameof(TimeStep));
            timeStep = value;
        }
    }
    /// <summary>Gets or sets finite, bounded container acceleration in particle units.</summary>
    internal Vector2 ContainerAcceleration
    {
        get => acceleration;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.Length() > 12.001f)
                throw new ArgumentOutOfRangeException(nameof(ContainerAcceleration));
            acceleration = value;
        }
    }
    /// <summary>Gets or sets prediction (zero), contacts (one), or finalization (two).</summary>
    internal int Pass
    {
        get => pass;
        set
        {
            if (value is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(Pass));
            pass = value;
        }
    }
    /// <summary>Gets or sets the fixed simulation count, bounded by the shared grain mesh capacity.</summary>
    internal int ParticleCount
    {
        get => particleCount;
        set
        {
            if (value is < 2 or > FoodGrainStateBuffers.ParticleCount) throw new ArgumentOutOfRangeException(nameof(ParticleCount));
            particleCount = value;
        }
    }
    #endregion
    #region Borrowed GPU Inputs
    /// <summary>Gets or sets the readable state texture, distinct from the feedback destination.</summary>
    internal int SourceStateTexture { get => source; set => source = RequireHandle(value); }
    /// <summary>Gets or sets storage for one eight-float output record per particle.</summary>
    internal int FeedbackBuffer { get => destination; set => destination = RequireHandle(value); }
    /// <summary>Gets or sets the simulation-owned feedback object.</summary>
    internal int FeedbackObject { get => feedback; set => feedback = RequireHandle(value); }
    /// <summary>Gets or sets the empty point-index vertex array.</summary>
    internal int VertexArray { get => vertexArray; set => vertexArray = RequireHandle(value); }
    #endregion
    #region Compilation and Execution
    /// <summary>Links the particle program with feedback outputs and validates its linked contract once.</summary>
    public override bool Compile()
    {
        if (GeometryShader is not null) throw new InvalidOperationException("Grain simulation uses vertex invocations only.");
        if (!SimulationShaderCompiler.Compile(this, varyings)) return false;
        try
        {
            foreach (string name in new[] { "state", "particleCount", "pass", "timeStep", "containerAcceleration" })
                if (!HasUniform(name)) throw new InvalidOperationException($"Grain solver missing '{name}'.");
            GL.GetProgram(ProgramId, GetProgramParameterName.ActiveUniforms, out int count);
            for (int i = 0; i < count; i++)
            {
                string name = GL.GetActiveUniform(ProgramId, i, out int size, out ActiveUniformType type);
                var expected = name switch
                {
                    "state" => ActiveUniformType.SamplerBuffer,
                    "particleCount" or "pass" => ActiveUniformType.Int,
                    "timeStep" => ActiveUniformType.Float,
                    "containerAcceleration" => ActiveUniformType.FloatVec2,
                    _ => throw new InvalidOperationException($"Unexpected grain uniform '{name}'.")
                };
                if (size != 1 || type != expected) throw new InvalidOperationException("Invalid grain uniform type.");
            }
            GL.GetProgram(ProgramId, GetProgramParameterName.TransformFeedbackVaryings, out int fields);
            if (fields != 2) throw new InvalidOperationException("Invalid grain feedback record.");
            for (int i = 0; i < fields; i++)
            {
                GL.GetTransformFeedbackVarying(ProgramId, i, 64, out _, out int size, out TransformFeedbackType type, out string name);
                if (size != 1 || type != TransformFeedbackType.FloatVec4 || name != varyings[i])
                    throw new InvalidOperationException("Grain feedback requires two ordered vec4 fields.");
            }
            return true;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Runs one caller-selected pass, releasing only owned scratch bindings without querying GPU state.</summary>
    internal void Advance()
    {
        if (Disposed || ProgramId <= 0 || source == 0 || destination == 0 || feedback == 0 || vertexArray == 0
            || !ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, this))
            throw new InvalidOperationException("Activate and bind a prepared grain solver before advancing.");
        bool begun = false;
        try
        {
            Uniform("state", 0);
            Uniform("particleCount", ParticleCount);
            Uniform("pass", pass);
            Uniform("timeStep", timeStep);
            Uniform("containerAcceleration", acceleration.X, acceleration.Y);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.TextureBuffer, source);
            GL.BindVertexArray(vertexArray);
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, destination);
            GL.Enable(EnableCap.RasterizerDiscard);
            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points);
            begun = true;
            GL.DrawArrays(PrimitiveType.Points, 0, ParticleCount);
        }
        finally
        {
            try { if (begun) GL.EndTransformFeedback(); }
            finally
            {
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
    /// <summary>Rejects missing borrowed GPU handles before submission.</summary>
    private static int RequireHandle(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
    #endregion
}
